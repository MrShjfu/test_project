import type { ButtonHTMLAttributes, ReactNode } from 'react';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
}

/** Minimal shared button used across `libs/web/feature-*` (ADR-008: shared UI lives here). */
export function Button({ children, type = 'button', ...rest }: ButtonProps) {
  return (
    <button type={type} {...rest}>
      {children}
    </button>
  );
}

export default Button;
