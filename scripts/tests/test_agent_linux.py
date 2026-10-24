"""Opt-in real Docker/firewall checks on disposable Linux CI, without provider calls."""
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import agent_sandbox as sandbox


@unittest.skipUnless(sys.platform == 'linux' and os.environ.get('AGENT_LINUX_INTEGRATION') == '1', 'requires opt-in disposable Linux Docker host')
class LinuxIsolation(unittest.TestCase):
    def test_restricted_container_enforces_boundaries(self):
        isolation = sandbox.Isolation()
        name = isolation.tag + '-restrictions'
        isolation.containers.append(name)
        probe = r'''
import os,socket
from pathlib import Path
assert os.getuid()==65532
assert not any(k in os.environ for k in ('GH_TOKEN','GITHUB_TOKEN','DEEPSEEK_API_KEY'))
for p in ('/root-probe','/runtime/agent_contract.py'):
    try:
        fd=os.open(p,os.O_WRONLY|os.O_CREAT,0o600);os.close(fd)
    except OSError: pass
    else: raise AssertionError('write allowed: '+p)
assert Path('/sys/fs/cgroup/memory.max').read_text().strip()=='4294967296'
assert Path('/sys/fs/cgroup/pids.max').read_text().strip()=='128'
mounts=Path('/proc/mounts').read_text().splitlines()
assert all(any(line.split()[1]==p and line.split()[2]=='tmpfs' for line in mounts) for p in ('/work','/tmp'))
assert set(os.listdir('/sys/class/net'))=={'lo'}
Path('/work/writable').write_text('synthetic')
print('boundaries enforced')
'''
        try:
            output = isolation.run(sandbox.restricted(name, sandbox.image('AGENT_PYTHON_IMAGE')) + ['-c', probe], timeout=40)
            self.assertIn(b'boundaries enforced', output)
        finally:
            isolation.cleanup()

    def test_real_gateway_network_blocks_host_and_other_interface(self):
        isolation = sandbox.Isolation()
        try:
            # generation_network probes TCP only. It never requests completion.
            with patch.dict(os.environ, {'DEEPSEEK_API_KEY':'synthetic-not-a-provider-key'}):
                network = isolation.generation_network(sandbox.image('AGENT_PYTHON_IMAGE'))
            self.assertEqual(network, isolation.tag + '-internal')
            self.assertEqual(len(isolation.rules), 4)
        finally:
            isolation.cleanup()


if __name__ == '__main__':
    unittest.main()
