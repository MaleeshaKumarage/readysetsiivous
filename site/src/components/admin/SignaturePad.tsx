'use client';

import { useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export default function SignaturePad({ onExport }: { onExport: (png: string) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [mode, setMode] = useState<'draw' | 'type'>('draw');
  const [typed, setTyped] = useState('');
  const drawing = useRef(false);

  function exportPng() {
    const canvas = canvasRef.current!;
    onExport(canvas.toDataURL('image/png'));
  }

  function start(e: React.PointerEvent) { drawing.current = true; draw(e); }
  function draw(e: React.PointerEvent) {
    if (!drawing.current) return;
    const c = canvasRef.current!;
    const rect = c.getBoundingClientRect();
    const x = e.clientX - rect.left, y = e.clientY - rect.top;
    const ctx = c.getContext('2d')!;
    ctx.strokeStyle = '#000'; ctx.lineWidth = 2; ctx.lineCap = 'round';
    ctx.lineTo(x, y); ctx.stroke(); ctx.beginPath(); ctx.moveTo(x, y);
  }
  function end() { drawing.current = false; }

  function renderTyped() {
    const c = canvasRef.current!;
    const ctx = c.getContext('2d')!;
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.font = 'italic 28px "Segoe Script", cursive';
    ctx.fillText(typed, 10, 50);
    exportPng();
  }

  return (
    <div className="space-y-2">
      <div className="flex gap-2">
        <Button variant={mode === 'draw' ? 'default' : 'outline'} size="sm" onClick={() => setMode('draw')}>Draw</Button>
        <Button variant={mode === 'type' ? 'default' : 'outline'} size="sm" onClick={() => setMode('type')}>Type</Button>
      </div>
      {mode === 'draw' ? (
        <canvas
          ref={canvasRef} width={400} height={120}
          className="rounded-md border bg-white"
          onPointerDown={start} onPointerMove={draw} onPointerUp={end}
        />
      ) : (
        <Input value={typed} onChange={(e) => setTyped(e.target.value)} placeholder="Type your name" />
      )}
      <Button size="sm" onClick={mode === 'draw' ? exportPng : renderTyped}>Use signature</Button>
    </div>
  );
}
