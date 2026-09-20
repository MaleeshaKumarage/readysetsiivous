'use client';

import { useCallback, useEffect, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { adminAgreements, type AgreementListItem } from '@/lib/adminApi';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

export default function AgreementsPage() {
  const [items, setItems] = useState<AgreementListItem[] | null>(null);
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState('');
  const [pdf, setPdf] = useState<File | null>(null);
  const [signers, setSigners] = useState<{ name: string; email: string }[]>([{ name: '', email: '' }]);

  const load = useCallback(async () => { setItems(await adminAgreements.list()); }, []);
  useEffect(() => { load(); }, [load]);

  async function create() {
    if (!pdf) return;
    await adminAgreements.create(title, pdf, signers.filter(s => s.name && s.email));
    setOpen(false); setTitle(''); setPdf(null); setSigners([{ name: '', email: '' }]);
    load();
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
