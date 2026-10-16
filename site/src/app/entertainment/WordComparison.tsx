type WordKind = 'unchanged' | 'incorrect' | 'corrected' | 'added' | 'removed';
type Word = { value: string; start: number; end: number };
type Operation = { kind: 'same' | 'delete' | 'insert'; original?: number; corrected?: number };

function wordsIn(text: string): Word[] {
  const words: Word[] = [];
  const pattern = /[\p{L}\p{M}\p{N}]+(?:['’][\p{L}\p{M}\p{N}]+)*/gu;
  for (const match of text.matchAll(pattern)) words.push({ value: match[0], start: match.index!, end: match.index! + match[0].length });
  return words;
}

function classify(original: Word[], corrected: Word[]) {
  const rows = original.length + 1; const columns = corrected.length + 1;
  const lcs = Array.from({ length: rows }, () => Array<number>(columns).fill(0));
  for (let i = original.length - 1; i >= 0; i--) for (let j = corrected.length - 1; j >= 0; j--) {
    lcs[i][j] = original[i].value === corrected[j].value ? 1 + lcs[i + 1][j + 1] : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
  }
  const operations: Operation[] = []; let i = 0; let j = 0;
  while (i < original.length || j < corrected.length) {
    if (i < original.length && j < corrected.length && original[i].value === corrected[j].value) operations.push({ kind: 'same', original: i++, corrected: j++ });
    else if (i < original.length && (j >= corrected.length || lcs[i + 1][j] >= lcs[i][j + 1])) operations.push({ kind: 'delete', original: i++ });
    else operations.push({ kind: 'insert', corrected: j++ });
  }
  const oldKinds: WordKind[] = Array(original.length).fill('removed'); const newKinds: WordKind[] = Array(corrected.length).fill('added');
  for (let index = 0; index < operations.length;) {
    if (operations[index].kind === 'same') { oldKinds[operations[index].original!] = 'unchanged'; newKinds[operations[index].corrected!] = 'unchanged'; index++; continue; }
    const start = index;
    while (index < operations.length && operations[index].kind !== 'same') index++;
    const deleted = operations.slice(start, index).filter(op => op.kind === 'delete');
    const inserted = operations.slice(start, index).filter(op => op.kind === 'insert');
    const paired = Math.min(deleted.length, inserted.length);
    for (let pair = 0; pair < paired; pair++) { oldKinds[deleted[pair].original!] = 'incorrect'; newKinds[inserted[pair].corrected!] = 'corrected'; }
  }
  return { oldKinds, newKinds };
}

const styles: Record<WordKind, string> = {
  unchanged: 'rounded bg-emerald-100 px-0.5 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100',
  incorrect: 'rounded bg-rose-200 px-0.5 text-rose-950 line-through decoration-2 dark:bg-rose-950 dark:text-rose-100',
  corrected: 'rounded bg-emerald-200 px-0.5 font-semibold text-emerald-950 dark:bg-emerald-900 dark:text-emerald-100',
  added: 'rounded bg-emerald-100 px-0.5 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100',
  removed: 'rounded bg-rose-100 px-0.5 text-rose-900 line-through decoration-2 dark:bg-rose-950 dark:text-rose-100',
};
const labels: Record<WordKind, string> = { unchanged: 'Unchanged word', incorrect: 'Incorrect original word', corrected: 'Corrected word', added: 'Added word', removed: 'Removed word' };

function renderSentence(text: string, words: Word[], kinds: WordKind[], side: string) {
  const parts: React.ReactNode[] = []; let cursor = 0;
  words.forEach((word, index) => {
    if (word.start > cursor) parts.push(text.slice(cursor, word.start));
    parts.push(<span key={`${side}-${index}`} className={styles[kinds[index]]} aria-label={`${labels[kinds[index]]}: ${word.value}`}>{word.value}</span>);
    cursor = word.end;
  });
  if (cursor < text.length) parts.push(text.slice(cursor));
  return parts;
}

export function WordComparison({ originalText, correctedText }: { originalText: string; correctedText: string }) {
  const original = wordsIn(originalText); const corrected = wordsIn(correctedText);
  const { oldKinds, newKinds } = classify(original, corrected);
  return <div data-testid="word-comparison" className="grid gap-3 sm:grid-cols-2">
    <p><span className="font-medium">Original answer: </span><span aria-label="Original sentence">{renderSentence(originalText, original, oldKinds, 'original')}</span></p>
    <p><span className="font-medium">Corrected answer: </span><span aria-label="Corrected sentence">{renderSentence(correctedText, corrected, newKinds, 'corrected')}</span></p>
  </div>;
}
