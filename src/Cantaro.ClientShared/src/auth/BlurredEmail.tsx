interface BlurredEmailProps {
  email: string;
  blur: boolean;
  className?: string;
}

export function BlurredEmail({ email, blur, className = '' }: BlurredEmailProps) {
  return <span className={`${blur ? 'select-none blur-sm' : ''} ${className}`.trim()} aria-label={blur ? 'Email address hidden' : email}>{email}</span>;
}
