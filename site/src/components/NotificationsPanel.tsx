'use client';
import { useEffect, useRef, useState } from 'react';
import { clearPushBinding, pushIdentity, pushRequest, readPushBinding, removePushDevice, savePushBinding, vapidBytes, type PushConfig } from '@/lib/pushNotifications';

export function NotificationsPanel() {
  const [config, setConfig] = useState<PushConfig | null>(null);
  const [device, setDevice] = useState<string | null>(null);
  const [message, setMessage] = useState('Checking notifications…');
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const registration = useRef<ServiceWorkerRegistration | null>(null);
  useEffect(() => {
    let active = true;
    async function prepare() {
      const ios = /iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
      if (ios && !(matchMedia('(display-mode: standalone)').matches || (navigator as any).standalone)) {
        setMessage('On iPhone or iPad, use iOS/iPadOS 16.4 or later. In Safari, tap Share → Add to Home Screen, then open the installed app to enable notifications.'); return;
      }
      if (!window.isSecureContext || !('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) { setMessage('Notifications are not supported in this browser. Try an up-to-date compatible browser or installed app.'); return; }
      try {
        const value = await pushRequest<PushConfig>();
        if (!active) return;
        setConfig(value);
        if (!value.available || !value.publicKey) { setMessage('Notifications are unavailable. Please try again later.'); return; }
        await navigator.serviceWorker.register('/sw.js');
        const sw = await navigator.serviceWorker.ready;
        if (!active) return;
        registration.current = sw;
        const binding = readPushBinding();
        let subscription = await sw.pushManager.getSubscription();
        if (subscription && (!binding || binding.identity !== pushIdentity())) {
          if (!await subscription.unsubscribe()) throw new Error('Could not clear the previous account’s browser subscription. Please retry.');
          subscription = null; clearPushBinding();
        }
        const enrolled = binding?.identity === pushIdentity() && subscription && value.devices.some(d => d.id === binding.id && d.enabled);
        if (enrolled) setDevice(binding.id);
        setReady(true);
        setMessage(Notification.permission === 'denied' ? 'Notifications are blocked. Allow notifications in your browser or device settings, then reload this page.' : enrolled ? 'Notifications are enabled on this device.' : 'Enable alerts for assigned work, schedule changes and shift reminders. Administrators also receive new partner request alerts.');
      } catch (error) { if (active) setMessage(error instanceof Error ? error.message : 'Notification setup failed. Reload to retry.'); }
    }
    void prepare(); return () => { active = false; };
  }, []);
  const enable = () => {
    // Keep permission directly within the tap gesture (required by iOS).
    let permission: Promise<NotificationPermission>;
    try { permission = Notification.requestPermission(); }
    catch { setMessage('Could not request permission. Try opening the installed app and retry.'); return; }
    setBusy(true);
    void (async () => {
      try {
        if (await permission !== 'granted') { setMessage('Notifications were not allowed. You can enable them in browser or device settings.'); return; }
        const sw = registration.current!;
        let subscription = await sw.pushManager.getSubscription();
        const key = vapidBytes(config!.publicKey);
        if (subscription?.options.applicationServerKey && !Array.from(new Uint8Array(subscription.options.applicationServerKey)).every((b, i) => b === key[i])) { await subscription.unsubscribe(); subscription = null; }
        subscription ||= await sw.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key as Uint8Array<ArrayBuffer> });
        let enrolled: { id: string };
        try { enrolled = await pushRequest('/subscription', 'PUT', subscription.toJSON()); }
        catch (error) {
          if ((error as { status?: number }).status !== 409) throw error;
          if (!await subscription.unsubscribe()) throw new Error('Could not renew browser subscription. Please retry.');
          clearPushBinding();
          subscription = await sw.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key as Uint8Array<ArrayBuffer> });
          enrolled = await pushRequest('/subscription', 'PUT', subscription.toJSON());
        }
        savePushBinding(enrolled.id); setDevice(enrolled.id); setMessage('Notifications are enabled on this device.');
      } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not enable notifications. Please retry.'); }
      finally { setBusy(false); }
    })();
  };
  const action = async (test: boolean) => {
    setBusy(true);
    try {
      if (test) { const result = await pushRequest<{ accepted: boolean }>(`/subscription/${encodeURIComponent(device!)}/test`, 'POST'); setMessage(result.accepted === true ? 'Test notification accepted for sending. This does not confirm delivery or display.' : 'The test notification was not accepted. Please try again or disable and re-enable notifications on this device.'); }
      else { await removePushDevice(); setDevice(null); setMessage('Notifications are disabled on this device. Your other devices are unchanged.'); }
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Could not update notifications. Please retry.'); }
    finally { setBusy(false); }
  };
  const button = 'min-h-11 rounded border border-gray-400 px-4 py-2 text-sm font-medium disabled:opacity-50';
  return <section id="notifications" aria-labelledby="notifications-title" className="rounded-xl border border-gray-200 dark:border-gray-700 p-4 space-y-3" aria-busy={busy}>
    <h2 id="notifications-title" className="text-lg font-semibold">Notifications</h2>
    <p role="status" className="text-sm" aria-live="polite">{message}</p>
    <div className="flex flex-wrap gap-3">{device ? <><button className={button} disabled={busy} onClick={() => void action(true)}>Send test notification</button><button className={button} disabled={busy} onClick={() => void action(false)}>Disable on this device</button></> : <button className={button} disabled={!ready || busy || (typeof Notification !== 'undefined' && Notification.permission === 'denied')} onClick={enable}>{busy ? 'Enabling…' : 'Enable notifications'}</button>}</div>
  </section>;
}
