'use client';

import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export interface SignaturePadHandle {
  clear: () => void;
  getPng: () => string;
}

function SignaturePad(
  _props: object,
  ref: React.Ref<SignaturePadHandle>
) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const [mode, setMode] = useState<'draw' | 'type'>('draw');
  const [typed, setTyped] = useState('');
  const drawing = useRef(false);
  const hasInk = useRef(false);

  // Size the canvas to its container, accounting for device pixel ratio so ink is crisp.
  useEffect(() => {
    const c = canvasRef.current;
    const wrap = wrapRef.current;
    if (!c || !wrap) return;
    const resize = () => {
      const dpr = window.devicePixelRatio || 1;
      const w = wrap.clientWidth;
      const h = 140;
      c.width = Math.round(w * dpr);
      c.height = Math.round(h * dpr);
      c.style.width = `${w}px`;
      c.style.height = `${h}px`;
      const ctx = c.getContext('2d');
      if (ctx) ctx.scale(dpr, dpr);
    };
    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(wrap);
    return () => ro.disconnect();
  }, []);

  function point(e: React.PointerEvent) {
    const c = canvasRef.current;
    if (!c) return { x: 0, y: 0 };
    const rect = c.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  }

  function ctx2d() {
    const c = canvasRef.current;
    if (!c) return null;
    const ctx = c.getContext('2d');
    if (!ctx) return null;
    ctx.strokeStyle = '#000';
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    return ctx;
  }

  function clearCanvas() {
    const c = canvasRef.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, c.width, c.height);
    const dpr = window.devicePixelRatio || 1;
    ctx.scale(dpr, dpr);
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
        ctx.font = 'italic 28px "Segoe Script", cursive';
        ctx.fillText(typed, 10, 50);
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
      <div ref={wrapRef} className={mode === 'draw' ? '' : 'hidden'}>
        <canvas
          ref={canvasRef}
          className="block touch-none select-none rounded-md border bg-white"
          onPointerDown={start}
          onPointerMove={move}
          onPointerUp={end}
          onPointerCancel={end}
        />
      </div>
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
