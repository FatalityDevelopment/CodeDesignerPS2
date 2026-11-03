/** Display names for the CheatCodeArchive's Database/ folders. Unknown folders show their raw name. */
const CATEGORY_LABELS: Record<string, string> = {
  'MEMORY-PS2-GSHI-DB': 'PS2 · GameShark',
  'MEMORY-PS2-CODEBREAKER': 'PS2 · CodeBreaker',
  'MEMORY-PS2-XPLODER-DB-[EU]': 'PS2 · Xploder (EU)',
  'MEMORY-PS2-CMP': 'PS2 · CMP',
  'SAVES-PS2': 'PS2 · Saves',
  'MEMORY-PS1': 'PS1',
  'PS3-WIP': 'PS3 · Work in progress',
  'SAVES-PS3': 'PS3 · Saves',
  'PS4-WIP': 'PS4 · Work in progress',
  'SAVES-PS4-SAVEWIZARD-CLEAN': 'PS4 · Saves (Save Wizard)',
  'SAVES-PSP': 'PSP · Saves',
  'SAVES-PSV': 'PS Vita · Saves',
  'MEMORY-GBA': 'Game Boy Advance',
  'MEMORY-NDS': 'Nintendo DS',
  'MEMORY-GEN': 'Genesis',
  'MEMORY-WII': 'Wii',
};

export function categoryLabel(category: string): string {
  return CATEGORY_LABELS[category] ?? category;
}
