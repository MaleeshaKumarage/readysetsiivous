#!/usr/bin/env python3
"""Validate the protected API-only tenant identity env_file without printing its values."""
import argparse
import os
from pathlib import Path
import re
import stat

MAX_BYTES = 256 * 1024
KEY = re.compile(r'Auth__Keycloak__TenantAccounts__([a-z0-9][a-z0-9-]{2,62})__(ClientId|ClientSecret)')


def validate_content(content, platform_realm='master'):
    pairs = {}
    for line in content.splitlines():
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        match = KEY.fullmatch(key)
        if not separator or not match or key != key.strip() or not value or value != value.strip():
            raise ValueError('Identity file must contain only explicit unquoted tenant account entries')
        realm, field = match.groups()
        if realm in ('master', platform_realm):
            raise ValueError('Platform realm credentials cannot be placed in the tenant identity file')
        entry = pairs.setdefault(realm, {})
        if field in entry:
            raise ValueError('Duplicate tenant credential field')
        if field == 'ClientId':
            if not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', value) or value in ('realm-management','account','admin-cli','cleaning-suite-web'):
                raise ValueError('Use a dedicated canonical tenant runtime client name')
        elif not re.fullmatch(r'[A-Za-z0-9_~./+=-]{16,}', value):
            raise ValueError('Use an unquoted opaque runtime secret without Compose interpolation characters')
        entry[field] = value
    if not pairs or any(set(pair) != {'ClientId','ClientSecret'} for pair in pairs.values()):
        raise ValueError('Every named tenant requires exactly one client ID and secret')
    return len(pairs)


def validate_file(filename, platform_realm='master'):
    if os.name != 'posix' or not hasattr(os, 'O_NOFOLLOW'):
        raise ValueError('Validate the runtime file on the supported POSIX deployment host')
    path = Path(filename)
    if not path.is_absolute():
        raise ValueError('Identity file path must be absolute')
    repository = Path(__file__).resolve().parents[2]
    resolved = path.resolve(strict=True)
    if path != resolved:
        raise ValueError("Use the canonical identity file path without symlink components")
    if resolved == repository or repository in resolved.parents:
        raise ValueError('Identity file must remain outside the checkout')
    # Trusted directory ownership prevents replacement between this check and Compose startup.
    for parent in resolved.parents:
        info = parent.stat()
        if info.st_uid not in (0, os.geteuid()) or stat.S_IMODE(info.st_mode) & 0o022:
            raise ValueError('Identity file parent directories must be owned by root/current user and not writable by group/others')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or info.st_uid not in (0, os.geteuid()) or stat.S_IMODE(info.st_mode) not in (0o400, 0o600):
            raise ValueError('Identity file must be a regular single-link root/current-user file with mode0400 or0600')
        data = source.read(MAX_BYTES + 1)
        if len(data) > MAX_BYTES:
            raise ValueError('Identity file exceeds bounded size')
    return validate_content(data.decode('utf-8'), platform_realm)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--file', required=True)
    parser.add_argument('--platform-realm', default='master')
    args = parser.parse_args()
    try:
        count = validate_file(args.file, args.platform_realm)
    except (OSError, ValueError):
        parser.exit(1, 'Protected tenant identity configuration rejected; verify path, permissions and complete tenant credential pairs. No values printed.\n')
    print('Protected tenant identity configuration valid (' + str(count) + ' tenant accounts; values hidden)')


if __name__ == '__main__': main()
