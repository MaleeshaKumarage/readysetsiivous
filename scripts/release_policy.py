"""Reviewed application compatibility contract shared by build and deployment."""
import re

GROUP = re.compile(r'[a-z0-9][a-z0-9._-]{0,63}')
SHA = re.compile(r'[0-9a-f]{40}')


def validate_policy(policy, *, allow_legacy=False):
    if (type(policy) is not dict or type(policy.get('schema')) is not int
            or policy.get('approved') is not True
            or policy.get('schema_changes') != 'backward-compatible-reviewed'):
        raise RuntimeError('Release compatibility approval required before deployment')
    if policy['schema'] == 1 and allow_legacy:
        baselines = policy.get('rollback_compatible_from')
    elif policy['schema'] == 2:
        group = policy.get('compatibility_group')
        if not isinstance(group, str) or not GROUP.fullmatch(group) or group == 'development':
            raise RuntimeError('A reviewed production compatibility group is required')
        baselines = policy.get('legacy_rollback_compatible_from', [])
    else:
        raise RuntimeError('Unsupported release compatibility policy schema')
    if type(baselines) is not list or any(not isinstance(value, str) or not SHA.fullmatch(value) for value in baselines):
        raise RuntimeError('Invalid legacy rollback compatibility list')
    return policy
