'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { toast } from 'sonner';
import { Plus, Copy, Trash2 } from 'lucide-react';
import { adminAgreements, adminTenant, type AgreementListItem, type AgreementDetail } from '@/lib/adminApi';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

export default function AgreementsPage() {
  const { lang } = useParams<{ lang: string }>();
  const [items, setItems] = useState<AgreementListItem[] | null>(null);
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState('');
  const [pdf, setPdf] = useState<File | null>(null);
  const [signers, setSigners] = useState<{ name: string; email: string }[]>([{ name: '', email: '' }]);
  const [detail, setDetail] = useState<AgreementDetail | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [companyName, setCompanyName] = useState('ReadySetSiivous');

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    adminTenant.get().then((t) => { if (t?.companyName) setCompanyName(t.companyName); });
  }, []);

  async function create() {
    if (!pdf) return;
    await adminAgreements.create(title, pdf, signers.filter(s => s.name && s.email));
    setOpen(false); setTitle(''); setPdf(null); setSigners([{ name: '', email: '' }]);
    load();
  }

  async function openDetail(id: string) {
    const d = await adminAgreements.get(id);
    if (d) { setDetail(d); setDetailOpen(true); }
  }

  async function copyLink(token: string) {
    const url = `${window.location.origin}/${lang}/sign?token=${token}`;
    try {
      await navigator.clipboard.writeText(url);
      toast.success('Link copied');
    } catch {
      toast.error('Copy failed');
    }
  }

  async function copyEmail(signerName: string, token: string) {
    const url = `${window.location.origin}/${lang}/sign?token=${token}`;
    const text = [
      'Subject: Agreement ready to sign',
      '',
      `Hi ${signerName},`,
      '',
      `Your ${detail?.title ?? 'agreement'} with ${companyName} is ready for electronic signature. Open the link, read the agreement, and sign by drawing on the screen.`,
      '',
      `Open & sign: ${url}`,
    ].join('\n');
    try {
      await navigator.clipboard.writeText(text);
      toast.success('Email copied');
    } catch {
      toast.error('Copy failed');
    }
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Agreements</h1>
        <Button onClick={() => setOpen(true)}><Plus className="mr-1.5 h-4 w-4" />New agreement</Button>
      </div>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-xl">
          <DialogHeader><DialogTitle>New agreement</DialogTitle></DialogHeader>
          <div className="grid gap-4">
            <div className="space-y-1.5">
              <Label>Title</Label><Input value={title} onChange={(e) => setTitle(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label>PDF</Label>
              <input type="file" accept=".pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} className="text-xs" />
            </div>
            <div className="space-y-2">
              <Label>Signers</Label>
              {signers.map((s, i) => (
                <div key={i} className="flex gap-2">
                  <Input placeholder="Name" value={s.name} onChange={(e) => {
                    const n = [...signers]; n[i].name = e.target.value; setSigners(n);
                  }} />
                  <Input placeholder="Email" value={s.email} onChange={(e) => {
                    const n = [...signers]; n[i].email = e.target.value; setSigners(n);
                  }} />
                  <Button variant="ghost" size="icon" onClick={() => setSigners(signers.filter((_, j) => j !== i))}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
              <Button variant="outline" size="sm" onClick={() => setSigners([...signers, { name: '', email: '' }])}>
                <Plus className="mr-1.5 h-3.5 w-3.5" />Add signer
              </Button>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
            <Button onClick={create} disabled={!pdf || !title}>Create</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={detailOpen} onOpenChange={setDetailOpen}>
        <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-xl">
          <DialogHeader><DialogTitle>{detail?.title ?? 'Agreement'}</DialogTitle></DialogHeader>
          <div className="space-y-3">
            {detail?.signers.map((s) => (
              <div key={s.id} className="flex items-center gap-3 rounded-lg border p-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{s.name}</div>
                  <div className="truncate text-xs text-muted-foreground">{s.email}</div>
                </div>
                <Badge variant={s.status === 'Signed' ? 'default' : 'secondary'}>{s.status}</Badge>
                <div className="flex flex-col gap-1">
                  <Button variant="outline" size="sm" onClick={() => copyLink(s.token)}>
                    <Copy className="mr-1.5 h-3.5 w-3.5" />Copy link
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => copyEmail(s.name, s.token)}>
                    <Copy className="mr-1.5 h-3.5 w-3.5" />Copy email
                  </Button>
                </div>
              </div>
            ))}
            {detail && detail.signers.length === 0 && (
              <p className="text-sm text-muted-foreground">No signers.</p>
            )}
          </div>
        </DialogContent>
      </Dialog>

      <div className="rounded-xl border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Title</TableHead><TableHead>Status</TableHead>
              <TableHead>Signed</TableHead><TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items?.map((a) => (
              <TableRow key={a.id}>
                <TableCell className="font-medium">{a.title}</TableCell>
                <TableCell>
                  <Badge variant={a.status === 'Completed' ? 'default' : 'secondary'}>{a.status}</Badge>
                </TableCell>
                <TableCell>{a.signedCount}/{a.signerCount}</TableCell>
                <TableCell className="text-right">
                  <Button variant="ghost" size="sm" onClick={() => openDetail(a.id)}>View</Button>
                  {a.status === 'Completed' && (
                    <Button variant="ghost" size="sm" onClick={() => adminAgreements.downloadDocument(a.id)}>Download</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
