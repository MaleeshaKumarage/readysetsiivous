'use client';

import { forwardRef, useImperativeHandle, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export interface SignaturePadHandle {
  clear: () => void;
  getPng: () => string;
}

// Fixed internal resolution, independent of container width and DPR, so the exported
// PNG stays well under the API's 1200x400 limit on any screen.
const W = 800;
const H = 200;

function SignaturePad(
  _props: object,
  ref: React.Ref<SignaturePadHandle>
) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [mode, setMode] = useState<'draw' | 'type'>('draw');
  const [typed, setTyped] = useState('');
  const drawing = useRef(false);
  const hasInk = useRef(false);

  function point(e: React.PointerEvent) {
    const c = canvasRef.current;
    if (!c) return { x: 0, y: 0 };
    const rect = c.getBoundingClientRect();
    return {
      x: (e.clientX - rect.left) * (W / rect.width),
      y: (e.clientY - rect.top) * (H / rect.height),
    };
  }

  function ctx2d() {
    const c = canvasRef.current;
    if (!c) return null;
    const ctx = c.getContext('2d');
    if (!ctx) return null;
    ctx.strokeStyle = '#000';
    ctx.lineWidth = 3;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    return ctx;
  }

  function clearCanvas() {
    const c = canvasRef.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    ctx.clearRect(0, 0, W, H);
    ctx.beginPath();
  }

  useImperativeHandle(ref, () => ({
    clear: () => {
      clearCanvas();
      setTyped('');
      drawing.current = false;
      hasInk.current = false;
    },
    getPng: () => {
      const c = canvasRef.current;
      if (!c) return '';
      if (mode === 'type') {
        if (!typed.trim()) return '';
        const ctx = c.getContext('2d');
        if (!ctx) return '';
        clearCanvas();
        ctx.font = 'italic 56px "Segoe Script", cursive';
        ctx.fillText(typed, 10, 130);
        hasInk.current = true;
      }
      if (!hasInk.current && mode === 'draw') return '';
      return c.toDataURL('image/png');
    },
  }));

  function start(e: React.PointerEvent) {
    const ctx = ctx2d();
    if (!ctx) return;
    drawing.current = true;
    canvasRef.current?.setPointerCapture(e.pointerId);
    const { x, y } = point(e);
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x, y);
    ctx.stroke();
    hasInk.current = true;
  }

  function move(e: React.PointerEvent) {
    if (!drawing.current) return;
    const ctx = ctx2d();
    if (!ctx) return;
    const { x, y } = point(e);
    ctx.lineTo(x, y);
    ctx.stroke();
  }

  function end(e: React.PointerEvent) {
    drawing.current = false;
    try { canvasRef.current?.releasePointerCapture(e.pointerId); } catch { /* noop */ }
  }

  return (
    <div className="space-y-2">
      <div className="flex gap-2">
        <Button
          size="sm"
          onClick={() => setMode('draw')}
          className={mode === 'draw'
            ? 'bg-primary text-primary-foreground'
            : 'bg-muted text-muted-foreground hover:bg-muted/80'}
        >Draw</Button>
        <Button
          size="sm"
          onClick={() => setMode('type')}
          className={mode === 'type'
            ? 'bg-primary text-primary-foreground'
            : 'bg-muted text-muted-foreground hover:bg-muted/80'}
        >Type</Button>
      </div>
      <canvas
        ref={canvasRef} width={W} height={H}
        className={`block h-auto w-full touch-none select-none rounded-md border bg-white ${mode === 'draw' ? '' : 'hidden'}`}
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={end}
        onPointerCancel={end}
      />
      <Input
        value={typed}
        onChange={(e) => setTyped(e.target.value)}
        placeholder="Type your name"
        className={mode === 'type' ? '' : 'hidden'}
      />
    </div>
  );
}

export default forwardRef(SignaturePad);
