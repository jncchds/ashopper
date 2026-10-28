import { useEffect, useRef, type ReactNode } from 'react';
import { Loader2, X } from 'lucide-react';
import clsx from 'clsx';
import { useI18n } from '../i18n';

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={clsx('animate-spin text-accent', className ?? 'h-6 w-6')} aria-hidden />;
}

export function Loading() {
  const { t } = useI18n();
  return (
    <div className="flex items-center justify-center gap-3 py-16 muted" role="status">
      <Spinner />
      <span>{t('common.loading')}</span>
    </div>
  );
}

export function ErrorBox({ error }: { error: unknown }) {
  const { t } = useI18n();
  const message = error instanceof Error ? error.message : String(error);
  return (
    <div className="rounded-lg border border-danger/30 bg-danger/10 p-4 text-sm text-danger" role="alert">
      <strong>{t('common.error')}:</strong> {message}
    </div>
  );
}

export function Empty({ children }: { children?: ReactNode }) {
  const { t } = useI18n();
  return <div className="py-16 text-center muted">{children ?? t('common.nothing')}</div>;
}

export function PageTitle({ children, actions, subtitle }: { children: ReactNode; actions?: ReactNode; subtitle?: ReactNode }) {
  return (
    <div className="mb-6">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="min-w-0 text-[1.4rem] font-bold">{children}</h1>
        {actions && <div className="ml-auto flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {subtitle && <p className="mt-1 text-sm muted">{subtitle}</p>}
    </div>
  );
}

/** A titled card section, as on the nopds settings page. */
export function Section({ title, hint, actions, children, className }: {
  title: ReactNode;
  hint?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={clsx('card p-5', className)}>
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <h2 className="text-lg font-semibold">{title}</h2>
        {actions && <div className="ml-auto flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {hint && <p className="-mt-2 mb-3 text-sm muted">{hint}</p>}
      {children}
    </section>
  );
}

/** Segmented control (nopds ViewToggle look). */
export function Segmented<T extends string | number>({ value, options, onChange, label }: {
  value: T;
  options: { value: T; label: ReactNode }[];
  onChange: (v: T) => void;
  label?: string;
}) {
  return (
    <div className="segmented" role="group" aria-label={label}>
      {options.map((o) => (
        <button
          key={String(o.value)}
          type="button"
          className={clsx('segment', o.value === value && 'segment-on')}
          aria-pressed={o.value === value}
          onClick={() => onChange(o.value)}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

/** Native <dialog> modal: focus trap, Esc and backdrop come from the browser. */
export function Modal({ open, onClose, title, children, wide }: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const { t } = useI18n();
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => e.target === ref.current && onClose()}
      className={clsx(
        'm-auto rounded-xl border border-line bg-surface p-0 text-fg shadow-xl backdrop:bg-black/40',
        wide ? 'w-[min(52rem,calc(100vw-2rem))]' : 'w-[min(40rem,calc(100vw-2rem))]',
      )}
    >
      <div className="flex items-center justify-between border-b border-line px-5 py-3">
        <h2 className="font-semibold">{title}</h2>
        <button type="button" className="btn-ghost px-2 py-1" onClick={onClose} aria-label={t('common.close')}>
          <X className="h-4 w-4" />
        </button>
      </div>
      <div className="max-h-[80vh] overflow-y-auto p-5">{open && children}</div>
    </dialog>
  );
}
