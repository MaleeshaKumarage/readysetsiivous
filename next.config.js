/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  images: {
    unoptimized: true,
  },
  trailingSlash: true,
  // Next 14's dev-mode vendor chunking misses framer-motion's nested
  // motion-dom/motion-utils sub-dependencies ("Cannot find module
  // './vendor-chunks/motion-dom.js'"). Bundling them through Next's own
  // pipeline fixes dev mode; production builds are unaffected.
  transpilePackages: ['framer-motion', 'motion-dom', 'motion-utils'],
};

module.exports = nextConfig;
