// Next 14 otherwise initializes its embedded webpack, while Storybook's plugins
// use the independently patched local webpack. Select one implementation before
// the Storybook CLI (and therefore any Next preset) is loaded.
process.env.NEXT_PRIVATE_LOCAL_WEBPACK = 'true';

const path = require('node:path');
const storybookRoot = path.dirname(require.resolve('storybook/package.json'));
require(path.join(storybookRoot, 'bin', 'index.cjs'));
