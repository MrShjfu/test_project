import { render, screen } from '@testing-library/react';
import App from './app';

describe('App', () => {
  it('renders the Helm Customer Portal brand placeholder', () => {
    render(<App />);

    expect(
      screen.getByRole('heading', { name: 'Helm Customer Portal' }),
    ).toBeInTheDocument();
  });
});
