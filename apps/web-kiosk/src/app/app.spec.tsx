import { render, screen } from '@testing-library/react';
import App from './app';

describe('App', () => {
  it('renders the Helm Factory Kiosk brand placeholder', () => {
    render(<App />);

    expect(
      screen.getByRole('heading', { name: 'Helm Factory Kiosk' }),
    ).toBeInTheDocument();
  });
});
