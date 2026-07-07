using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Helm.Core.Messaging;
using Helm.Crm.Contracts.Events;
using Xunit.Abstractions;

namespace Helm.DemoSlice.Tests;

/// <summary>
/// Proof-of-infrastructure demo slice, end to end: POST /api/v1/crm/customers writes Customer +
/// outbox row in Crm's schema (one transaction, ADR-004); OutboxRelay&lt;CrmDbContext&gt; picks the
/// row up and publishes it over a real RabbitMQ broker (Messaging:Provider=RabbitMQ,
/// Outbox:RelayEnabled=true — see DemoSliceFactory); Cpq's CustomerCreatedConsumer
/// (IdempotentConsumer&lt;CpqDbContext, CustomerCreated&gt;) receives it and writes cpq.customer_ref.
/// Then the same envelope (same EventId) is republished directly, proving the second delivery is a
/// no-op — the ProcessedEvent PK conflict absorbs at-least-once redelivery (ADR-004) rather than
/// re-running HandleAsync.
/// </summary>
public class OutboxEndToEndTests : IClassFixture<DemoSliceFactory>, IAsyncLifetime
{
    private readonly DemoSliceFactory _factory;
    private readonly ITestOutputHelper _output;

    public OutboxEndToEndTests(DemoSliceFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private record CreateCustomerRequest(string Name, string Email);
    private record CustomerDto(Guid Id, string Name, string Email);

    [Fact]
    public async Task Customer_created_event_is_delivered_to_cpq_exactly_once_despite_redelivery()
    {
        var client = _factory.AsCompany("doyle", "crm:write");

        var create = await client.PostAsJsonAsync("/api/v1/crm/customers",
            new CreateCustomerRequest("Doyle Demo Slice Co", "demo-slice@doyle.example"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await create.Content.ReadFromJsonAsync<CustomerDto>();
        customer.Should().NotBeNull();

        // Poll cpq.customer_ref until the async pipeline (outbox -> relay -> RabbitMQ -> consumer)
        // delivers, up to 10s total, checking every 300ms. Timing is recorded for the task report.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        long rowCount = 0;
        var pollCount = 0;
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            pollCount++;
            rowCount = await _factory.CountCpqAsync($"SELECT count(*) FROM cpq.customer_ref WHERE customer_id = '{customer!.Id}'");
            if (rowCount == 1) break;
            await Task.Delay(300);
        }

        var elapsed = DateTime.UtcNow - started;
        // Actual observed delivery latency is captured in the task-15 report from this test's
        // output (elapsed, pollCount) at verification time.
        _output.WriteLine(
            $"Customer {customer!.Id} delivered to cpq.customer_ref after {elapsed.TotalMilliseconds:F0}ms ({pollCount} polls).");

        rowCount.Should().Be(1, "the outbox relay should have delivered CustomerCreated to Cpq's consumer within 10s");

        // Re-publish the exact same envelope (same EventId => same CustomerCreated.EventId) directly
        // over a second RabbitMQ connection, simulating an at-least-once broker redelivery.
        // NOTE: EventId must match the ORIGINAL event's id (the one already recorded in
        // cpq.processed_events), not a fresh one — otherwise this only proves two different events
        // don't collide, not idempotent redelivery of the SAME event. Read back the original event id
        // from crm.outbox, which is that event's own id (OutboxWriter: "outbox row id IS the domain
        // event id").
        var originalEventId = await ReadOriginalEventIdAsync(customer.Id);
        var replayEnvelope = new EventEnvelope(
            originalEventId,
            nameof(CustomerCreated),
            JsonSerializer.Serialize(new CustomerCreated(originalEventId, customer.Id, "doyle", customer.Name)));

        await using var replayBus = await RabbitMqEventBus.ConnectAsync(_factory.RabbitMqUri, "demo-slice-test-replay");
        await replayBus.PublishAsync(replayEnvelope);

        await Task.Delay(2000);

        var rowCountAfterReplay = await _factory.CountCpqAsync(
            $"SELECT count(*) FROM cpq.customer_ref WHERE customer_id = '{customer.Id}'");
        rowCountAfterReplay.Should().Be(1,
            "redelivery of the same event id must be a no-op (processed_events PK conflict), not a repeat insert");
    }

    private async Task<Guid> ReadOriginalEventIdAsync(Guid customerId)
    {
        // crm.outbox.payload is jsonb containing the serialized CustomerCreated, whose CustomerId
        // field matches; the outbox row's own id equals the event's EventId (OutboxWriter contract).
        var idText = await _factory.ScalarStringAsync(
            $"SELECT id::text FROM crm.outbox WHERE payload->>'CustomerId' = '{customerId}'");
        return Guid.Parse(idText);
    }
}
