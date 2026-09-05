// Cleaning Suite API client. Public endpoints only — admin uses the token path.
// Static export must never call this at build time; fetch happens client-side.

export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "https://api.readysetsiivous.fi";
export const TENANT_SLUG = process.env.NEXT_PUBLIC_TENANT_SLUG ?? "readysetsiivous";

export interface PublicService {
  id: string;
  slug: string;
  category: string;
  name: string;
  description: string;
  durationMinutes: number;
  priceNet: number;
  vatRatePercent: number;
  currency: string;
}

export interface AvailabilitySlot {
  startTime: string;
  endTime: string;
}

export interface BookingRequest {
  localDate: string;
  startTime: string;
  serviceId: string;
  customerName: string;
  customerPhone: string;
  customerEmail: string;
  street: string;
  postalCode: string;
  city: string;
  notes?: string | null;
}

export interface BookingResult {
  bookingId: string;
  bookingNumber: string;
  customerReference: string;
}

async function getJson<T>(path: string): Promise<T | null> {
  try {
    const response = await fetch(`${API_URL}${path}`, {
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(8000),
    });
    if (!response.ok) return null;
    return (await response.json()) as T;
  } catch {
    return null;
  }
}

export async function fetchServices(lang: string): Promise<PublicService[] | null> {
  return getJson<PublicService[]>(
    `/api/v1/public/${TENANT_SLUG}/services?lang=${encodeURIComponent(lang)}`
  );
}

export async function fetchAvailability(
  localDate: string,
  serviceId: string
): Promise<AvailabilitySlot[] | null> {
  return getJson<AvailabilitySlot[]>(
    `/api/v1/public/${TENANT_SLUG}/availability?date=${encodeURIComponent(localDate)}&serviceId=${encodeURIComponent(serviceId)}`
  );
}

export async function createBooking(body: BookingRequest): Promise<BookingResult | null> {
  try {
    const response = await fetch(`${API_URL}/api/v1/public/${TENANT_SLUG}/bookings`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(10000),
    });
    if (!response.ok) return null;
    return (await response.json()) as BookingResult;
  } catch {
    return null;
  }
}
