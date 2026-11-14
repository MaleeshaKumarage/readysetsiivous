import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createTtlCache } from './ttlCache.ts';

test('a loaded value is reused until its ttl expires', async () => {
  let clock = 0;
  const cache = createTtlCache<string>(1000, () => clock);
  let loads = 0;
  const load = async () => { loads += 1; return `v${loads}`; };
  assert.equal(await cache.getOrLoad('k', load), 'v1');
  clock = 999;
  assert.equal(await cache.getOrLoad('k', load), 'v1');
  clock = 1000;
  assert.equal(await cache.getOrLoad('k', load), 'v2');
  assert.equal(loads, 2);
});

test('concurrent callers share one request', async () => {
  const cache = createTtlCache<string>(1000);
  let loads = 0;
  const load = () => new Promise<string>(resolve => { loads += 1; setTimeout(() => resolve('done'), 5); });
  const results = await Promise.all([cache.getOrLoad('k', load), cache.getOrLoad('k', load), cache.getOrLoad('k', load)]);
  assert.deepEqual(results, ['done', 'done', 'done']);
  assert.equal(loads, 1);
});

test('null results and failures are not cached', async () => {
  const cache = createTtlCache<string>(1000);
  let loads = 0;
  assert.equal(await cache.getOrLoad('k', async () => { loads += 1; return null; }), null);
  assert.equal(await cache.getOrLoad('k', async () => { loads += 1; return 'ok'; }), 'ok');
  await assert.rejects(cache.getOrLoad('f', async () => { throw new Error('down'); }));
  assert.equal(await cache.getOrLoad('f', async () => 'recovered'), 'recovered');
  assert.equal(loads, 2);
});
