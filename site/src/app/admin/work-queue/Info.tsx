/** Help text behind an "i": shown on hover or keyboard focus, as in the design. */
export function Info({ text }: { text: string }) {
  return (
    <span className="info">
      <button type="button" className="info-btn" aria-label="More information">
        <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="7" fill="none" stroke="currentColor" strokeWidth="1.5" /><path d="M8 7v4.5M8 4.5v.5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></svg>
      </button>
      <span className="tip" role="tooltip">{text}</span>
    </span>
  );
}
