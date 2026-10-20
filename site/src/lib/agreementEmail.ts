const htmlEntities: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
const escapeHtml = (value: string) => value.replace(/[&<>"']/g, character => htmlEntities[character]);

export function agreementEmail(title: string, signerName: string, link: string) {
  const name = escapeHtml(signerName);
  const agreement = escapeHtml(title);
  const url = escapeHtml(link);
  return {
    text: `Hei ${signerName},\n\nSopimus ”${title}” on valmis tarkistettavaksi ja allekirjoitettavaksi.\n\nAvaa sopimus: ${link}\n\nVoit tarkistaa asiakirjan ja allekirjoittaa sen verkkosivullamme. Älä välitä henkilökohtaista allekirjoituslinkkiä muille.\n\nYstävällisin terveisin,\nReadySet Siivous`,
    html: `<div lang="fi" style="font-family:Arial,Helvetica,sans-serif;background:#f3f5fa;padding:20px;color:#17213b;line-height:1.6;"><table role="presentation" style="width:100%;max-width:600px;border-collapse:collapse;margin:auto;background:#fff;"><tr><td style="background:#0a1741;color:#fff;padding:24px;border-bottom:4px solid #ddc067;"><p style="color:#ddc067;margin:0 0 8px;font-size:13px;">READYSET SIIVOUS</p><h1 style="font-size:24px;line-height:1.3;margin:0;">Sopimus allekirjoitettavaksi</h1></td></tr><tr><td style="padding:24px;"><p>Hei ${name},</p><p>Sopimus <strong>”${agreement}”</strong> on valmis tarkistettavaksi ja allekirjoitettavaksi.</p><p>Voit tarkistaa asiakirjan ja allekirjoittaa sen verkkosivullamme.</p><table role="presentation" style="border-collapse:collapse;margin:24px 0;"><tr><td style="background:#ddc067;border-radius:6px;"><a href="${url}" style="display:inline-block;padding:14px 24px;color:#0a1741;font-weight:bold;text-decoration:none;">Allekirjoita sopimus</a></td></tr></table><p style="font-size:13px;">Jos painike ei toimi, avaa tämä linkki selaimessasi:</p><p style="font-size:12px;word-break:break-all;"><a href="${url}" style="color:#0a1741;">${url}</a></p><p style="font-size:13px;color:#536078;">Älä välitä henkilökohtaista allekirjoituslinkkiä muille.</p><p>Ystävällisin terveisin,<br><strong>ReadySet Siivous</strong></p></td></tr></table></div>`,
  };
}
