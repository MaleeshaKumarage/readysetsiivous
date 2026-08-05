/**
 * WhatsApp integration utilities.
 *
 * Replace PHONE_NUMBER with the actual business WhatsApp number
 * in international format without the '+' or leading zeros.
 * Example: 358401234567 for a Finnish number +358 40 123 4567
 */
const WHATSAPP_PHONE = '358XXXXXXXXX'; // TODO: Replace with real number

interface QuoteData {
  service: string;
  size: string;
  city: string;
  date: string;
}

interface MessageLabels {
  greeting: string;
  serviceLabel: string;
  sizeLabel: string;
  cityLabel: string;
  dateLabel: string;
  languageNote: string;
}

/**
 * Build a pre-filled WhatsApp message from quote data and language-specific labels.
 */
export function buildWhatsAppMessage(
  data: QuoteData,
  labels: MessageLabels
): string {
  const lines = [
    labels.greeting,
    '',
    `*${labels.serviceLabel}:* ${data.service}`,
    `*${labels.sizeLabel}:* ${data.size}`,
    `*${labels.cityLabel}:* ${data.city}`,
    `*${labels.dateLabel}:* ${data.date}`,
    '',
    `_${labels.languageNote}_`,
  ];

  return lines.join('\n');
}

/**
 * Open WhatsApp with a pre-filled message.
 * Falls back to web if the device doesn't support the wa.me protocol.
 */
export function openWhatsApp(message: string): void {
  const encoded = encodeURIComponent(message);
  const url = `https://wa.me/${WHATSAPP_PHONE}?text=${encoded}`;

  // On mobile, wa.me links open the native app automatically.
  // On desktop they open WhatsApp Web.
  window.open(url, '_blank', 'noopener,noreferrer');
}

/**
 * Static WhatsApp chat link for buttons (no pre-filled text).
 */
export function getWhatsAppChatUrl(): string {
  return `https://wa.me/${WHATSAPP_PHONE}`;
}
