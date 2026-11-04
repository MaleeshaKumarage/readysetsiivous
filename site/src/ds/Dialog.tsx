'use client';

import { useEffect, useId, useRef, type ReactNode } from 'react';

/**
 * Modal dialog for portals without a component library. Focus moves into the dialog on open and returns to
 * the opener on close. Tab and Shift+Tab stay inside the dialog. Escape and a backdrop click close it. Content stays mounted only while open.
 */
export function Dialog({ open, title, onClose, className, children }: {
  open: boolean;
  title: string;
  onClose: () => void;
  className?: string;
  children: ReactNode;
}) {
  const titleId = useId();
  const panel = useRef<HTMLDivElement>(null);
  const close = useRef(onClose);
  close.current = onClose;

  useEffect(() => {
    if (!open) return;
    const opener = document.activeElement as HTMLElement | null;
    const first = panel.current?.querySelector<HTMLElement>('input, textarea, select, button:not([disabled])');
    (first ?? panel.current)?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { close.current(); return; }
      if (event.key !== 'Tab' || !panel.current) return;
      // aria-modal: keep Tab and Shift+Tab cycling inside the panel so background controls stay unreachable.
      const focusable = Array.from(panel.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
        .filter(element => element.offsetParent !== null || element === document.activeElement);
      if (!focusable.length) { event.preventDefault(); panel.current.focus(); return; }
      const first = focusable[0], last = focusable[focusable.length - 1];
      const active = document.activeElement as HTMLElement | null;
      const inside = !!active && panel.current.contains(active);
      if (event.shiftKey && (!inside || active === first || active === panel.current)) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && (!inside || active === last)) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('keydown', onKey); opener?.focus?.(); };
  }, [open]);

  if (!open) return null;
  return <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/40 p-4"
    // Inline margin beats parent spacing utilities (space-y-*), whose margin would leave an uncovered strip above a fixed overlay.
    style={{ margin: 0 }} onMouseDown={event => {
    // preventDefault stops the browser moving focus to the body after the dialog has returned it to the opener.
    if (event.target === event.currentTarget) { event.preventDefault(); close.current(); }
  }}>
    <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} className={className}>
      <h2 id={titleId}>{title}</h2>
      {children}
    </div>
  </div>;
}
