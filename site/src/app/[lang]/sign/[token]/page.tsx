import SignPageClient from './SignPageClient';

// The signer token is a runtime value from a per-signer link, so real tokens
// cannot be enumerated at build time. `output: export` requires at least one
// static path per dynamic route, so a placeholder token is generated to keep
// the build green; the client component reads the real token from the URL at
// runtime via useParams.
export function generateStaticParams() {
  return [
    { lang: 'fi', token: 'demo' },
    { lang: 'en', token: 'demo' },
    { lang: 'sv', token: 'demo' },
  ];
}

export default function SignPage() {
  return <SignPageClient />;
}
