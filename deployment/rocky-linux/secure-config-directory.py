#!/usr/bin/env python3
"""Secure the existing fixed host configuration tree without following links."""
import os
from pathlib import Path
import pwd
import stat
import sys

DIRECTORY_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
FILE_FLAGS = os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK


def secure_directory(path, uid, gid):
    path = Path(path)
    if not path.is_absolute() or '..' in path.parts:
        raise ValueError('absolute_configuration_path_required')
    descriptors = []
    entries = []
    try:
        parent = os.open('/', DIRECTORY_FLAGS)
        descriptors.append(parent)
        for component in path.parts[1:]:
            parent = os.open(component, DIRECTORY_FLAGS, dir_fd=parent)
            descriptors.append(parent)
        configuration = parent

        def visit(fd, depth):
            if depth > 8:
                raise ValueError('configuration_depth_exceeded')
            metadata = os.fstat(fd)
            if metadata.st_uid not in {0, uid}:
                raise ValueError('unexpected_configuration_owner')
            for name in sorted(os.listdir(fd)):
                if len(entries) >= 256:
                    raise ValueError('configuration_entry_budget_exceeded')
                before = os.stat(name, dir_fd=fd, follow_symlinks=False)
                if not (stat.S_ISREG(before.st_mode) or stat.S_ISDIR(before.st_mode)):
                    raise ValueError('configuration_links_or_special_files_forbidden')
                child = os.open(name, DIRECTORY_FLAGS if stat.S_ISDIR(before.st_mode) else FILE_FLAGS, dir_fd=fd)
                descriptors.append(child)
                actual = os.fstat(child)
                if (actual.st_dev, actual.st_ino) != (before.st_dev, before.st_ino):
                    raise ValueError('configuration_changed')
                if actual.st_uid not in {0, uid}:
                    raise ValueError('unexpected_configuration_owner')
                if stat.S_ISREG(actual.st_mode) and actual.st_nlink != 1:
                    raise ValueError('configuration_hard_link_forbidden')
                entries.append((fd, name, child, actual))
                if stat.S_ISDIR(actual.st_mode):
                    visit(child, depth + 1)

        visit(configuration, 0)
        # Validate the whole tree before making any permission changes.
        for fd, name, child, metadata in entries:
            current = os.stat(name, dir_fd=fd, follow_symlinks=False)
            if (current.st_dev, current.st_ino, current.st_nlink) != (metadata.st_dev, metadata.st_ino, metadata.st_nlink):
                raise ValueError('configuration_changed')
        os.fchown(configuration, uid, gid)
        os.fchmod(configuration, 0o700)
        for _, _, child, metadata in entries:
            os.fchown(child, uid, gid)
            os.fchmod(child, 0o700 if stat.S_ISDIR(metadata.st_mode) else 0o600)
        # A swapped tree is never reported as successfully secured.
        current = os.stat(path, follow_symlinks=False)
        actual = os.fstat(configuration)
        if (current.st_dev, current.st_ino) != (actual.st_dev, actual.st_ino):
            raise ValueError('configuration_changed')
        return len(entries)
    finally:
        for fd in reversed(descriptors):
            os.close(fd)


def main():
    if len(sys.argv) != 1 or os.geteuid() != 0:
        raise ValueError('fixed_host_root_execution_required')
    owner = pwd.getpwnam('opc')
    count = secure_directory('/opt/project-time-platform/config', owner.pw_uid, owner.pw_gid)
    print('HOST_CONFIG_PERMISSIONS=PASS entries=' + str(count))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('HOST_CONFIG_PERMISSIONS=FAILED type=' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
