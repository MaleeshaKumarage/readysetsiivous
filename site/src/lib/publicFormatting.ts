import type { Language } from '@/i18n';
import type { PublicService } from './api';

export const locale = (lang: Language) => ({ fi: 'fi-FI', en: 'en-GB', sv: 'sv-FI' })[lang];
export const money = (value: number, lang: Language, currency = 'EUR') =>
  new Intl.NumberFormat(locale(lang), { style: 'currency', currency }).format(value);
function cents(value: number) {
  const scaled = value * 100, floor = Math.floor(scaled);
  return (Math.abs(scaled - floor - .5) < 1e-8 ? floor + floor % 2 : Math.round(scaled)) / 100;
}
export const gross = (service: PublicService) => service.priceGross ??
  cents(cents(service.priceNet) + cents(service.priceNet * (service.vatRatePercent ?? 0) / 100));
export const helsinkiToday = () => new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Helsinki' }).format(new Date());
export function fullDate(value: string, lang: Language) {
  return new Intl.DateTimeFormat(locale(lang), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(`${value}T12:00:00Z`));
}
export function downloadConfirmation(text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
  const link = document.createElement('a'); link.href = url; link.download = 'readyset-booking.txt'; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
