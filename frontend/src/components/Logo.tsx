/** App mark: an emerald tile with a falling-then-rising price line. Same shape as public/favicon.svg. */
export function Logo({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" className={className} aria-hidden>
      <rect width="32" height="32" rx="7" fill="#0f8a5f" />
      <path d="M6 10 L13 18 L18 15 L26 23" fill="none" stroke="#fff" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
