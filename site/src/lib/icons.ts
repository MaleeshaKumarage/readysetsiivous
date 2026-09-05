// Lucide icon catalog for service cards. Names stored on the Service document;
// the website maps name → component with a fallback.

import {
  Home, Building2, Sparkles, Brush, Truck, KeyRound, ShieldCheck, Star,
  SprayCan, WashingMachine, Baby, Leaf, HeartHandshake, Sun, Droplets, Sofa,
  type LucideIcon,
} from 'lucide-react';

export const SERVICE_ICONS: Record<string, LucideIcon> = {
  Home, Building2, Sparkles, Brush, Truck, KeyRound, ShieldCheck, Star,
  SprayCan, WashingMachine, Baby, Leaf, HeartHandshake, Sun, Droplets, Sofa,
};

export function serviceIcon(name?: string): LucideIcon {
  return SERVICE_ICONS[name ?? ''] ?? Sparkles;
}
