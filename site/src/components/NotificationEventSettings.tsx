'use client';

import { useEffect, useState } from 'react';
import { notificationPreferencesApi, type NotificationEvent } from '@/lib/notificationPreferencesApi';

const GROUP_TITLE: Record<string, string> = { Cleaner: 'For cleaners', Staff: 'For staff' };

/**
 * Per-event choices for the signed-in person's role. The App switch turns the event off in the inbox and push.
 * The Email switch is shown for every event; email is only sent for events where it is available.
 */
export function NotificationEventSettings() {
  const [events, setEvents] = useState<NotificationEvent[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let current = true;
    notificationPreferencesApi.get()
      .then(result => { if (current) setEvents(result.events); })
      .catch(failure => { if (current) setError(failure instanceof Error ? failure.message : 'Could not load notification settings.'); });
    return () => { current = false; };
  }, []);

  const setApp = async (event: NotificationEvent, app: boolean) => {
    setBusy(event.key); setError('');
    // Show the choice straight away; if saving fails, the previous choice is restored.
    setEvents(current => current?.map(item => item.key === event.key ? { ...item, app } : item) ?? null);
    try {
      const result = await notificationPreferencesApi.setApp(event.key, app);
      setEvents(result.events);
    } catch (failure) {
      setEvents(current => current?.map(item => item.key === event.key ? { ...item, app: !app } : item) ?? null);
      setError(failure instanceof Error ? failure.message : 'Could not save this choice. Try again.');
    } finally {
      setBusy(null);
    }
  };

  if (error && !events) return <p role="alert" className="notify-empty">{error}</p>;
  if (!events) return <p role="status" className="notify-empty">Loading notification settings...</p>;

  const groups = Array.from(new Set(events.map(event => event.group)));
  return <section aria-labelledby="event-settings-heading" className="account-section">
    <h2 id="event-settings-heading">Choose what you are notified about</h2>
    <p className="account-muted">In the app turns alerts on or off in your inbox and on this device. Email is only sent for events where it is available.</p>
    {error && <p role="alert" className="partner-alert partner-alert-error">{error}</p>}
    {groups.map(group => <div key={group} className="account-event-group">
      <h3>{GROUP_TITLE[group] ?? group}</h3>
      <ul className="account-event-list">
        {events.filter(event => event.group === group).map(event => <li key={event.key} className="account-event">
          <div className="account-event-text">
            <span className="account-event-label" id={`event-${event.key}`}>{event.label}</span>
            <span className="account-muted">{event.description}</span>
          </div>
          <div className="account-event-switches">
            <label className="account-switch">
              <input type="checkbox" role="switch" aria-label={`${event.label}: in the app`}
                checked={event.app} disabled={busy === event.key} onChange={e => void setApp(event, e.target.checked)} />
              <span>In app</span>
            </label>
            <label className="account-switch account-switch-disabled">
              <input type="checkbox" role="switch" aria-label={`${event.label}: email`} checked={false} disabled
                title={event.emailAvailable ? undefined : 'Email for this event is coming soon'} />
              <span>Email{event.emailAvailable ? '' : ' (coming soon)'}</span>
            </label>
          </div>
        </li>)}
      </ul>
    </div>)}
  </section>;
}
