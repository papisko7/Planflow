// The 24 Google Calendar colours (plan point 14). The backend stores only the name (ItemColors.Keys); the shade is chosen
// here, so a dark theme could remap it later. null = "Default colour": the app's own look. Hex values were measured from
// Google Calendar screenshots (about +-3 per channel).
export interface PaletteColor {
  key: string;
  label: string;
  hex: string;
}

export const PALETTE: readonly PaletteColor[] = [
  { key: 'beetroot', label: 'Beetroot', hex: '#ad1158' },
  { key: 'cherry-blossom', label: 'Cherry blossom', hex: '#d7195f' },
  { key: 'flamingo', label: 'Flamingo', hex: '#e77c72' },
  { key: 'tomato', label: 'Tomato', hex: '#d50004' },
  { key: 'tangerine', label: 'Tangerine', hex: '#f55020' },
  { key: 'pumpkin', label: 'Pumpkin', hex: '#ef6c00' },
  { key: 'mango', label: 'Mango', hex: '#f19200' },
  { key: 'banana', label: 'Banana', hex: '#f6bf27' },
  { key: 'citron', label: 'Citron', hex: '#e5c441' },
  { key: 'avocado', label: 'Avocado', hex: '#c0ca33' },
  { key: 'pistachio', label: 'Pistachio', hex: '#7cb242' },
  { key: 'basil', label: 'Basil', hex: '#097f42' },
  { key: 'sage', label: 'Sage', hex: '#5cb37e' },
  { key: 'eucalyptus', label: 'Eucalyptus', hex: '#429488' },
  { key: 'peacock', label: 'Peacock', hex: '#4599df' },
  { key: 'cobalt', label: 'Cobalt', hex: '#5384ed' },
  { key: 'lavender', label: 'Lavender', hex: '#7b86c6' },
  { key: 'blueberry', label: 'Blueberry', hex: '#4350b0' },
  { key: 'wisteria', label: 'Wisteria', hex: '#b09ed8' },
  { key: 'amethyst', label: 'Amethyst', hex: '#956bab' },
  { key: 'grape', label: 'Grape', hex: '#822da4' },
  { key: 'cocoa', label: 'Cocoa', hex: '#74564b' },
  { key: 'graphite', label: 'Graphite', hex: '#616161' },
  { key: 'birch', label: 'Birch', hex: '#a59b8f' },
];

const BY_KEY = new Map(PALETTE.map((c) => [c.key, c]));

// The shade for a stored key; null for the default colour or a key this app version does not know (a newer server).
export function colorHex(key: string | null | undefined): string | null {
  return (key && BY_KEY.get(key)?.hex) || null;
}

export function colorLabel(key: string | null | undefined): string {
  return (key && BY_KEY.get(key)?.label) || 'Default colour';
}

// Dark or white text, whichever has the higher WCAG contrast ratio on the given background, so light colours such as banana,
// citron, wisteria or birch get dark text and the deep ones (tomato, graphite, ...) white.
export function readableTextColor(hex: string): string {
  const channel = (offset: number) => {
    const c = parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
  const DARK = '#111827';
  const darkLuminance = 0.009; // relative luminance of DARK
  const contrastWithWhite = 1.05 / (luminance + 0.05);
  const contrastWithDark = (luminance + 0.05) / (darkLuminance + 0.05);
  return contrastWithWhite >= contrastWithDark ? '#FFFFFF' : DARK;
}
