"""Build the complete web recipe using verified immutable Docker Hub mirror images."""
from pathlib import Path
import os
import subprocess
import tempfile

MIRRORS={
 '# syntax=docker/dockerfile:1':'# syntax=mirror.gcr.io/docker/dockerfile:1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e',
 'FROM node:24-alpine AS build':'FROM mirror.gcr.io/library/node:24-alpine@sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1 AS build',
 'FROM nginxinc/nginx-unprivileged:1.31.3-alpine AS runtime':'FROM mirror.gcr.io/nginxinc/nginx-unprivileged:1.31.3-alpine@sha256:f972e5322b9797dc2a6b830030094426437b1ae7032e4644496395336ac6fdac AS runtime'
}

def recipe(text):
    lines=text.splitlines(keepends=True)
    for old,new in MIRRORS.items():
        assert sum(line.rstrip('\n')==old for line in lines)==1,'Unregistered web base image'
        lines=[new+'\n' if line.rstrip('\n')==old else line for line in lines]
    return ''.join(lines)

if __name__=='__main__':
    with tempfile.TemporaryDirectory() as directory:
        dockerfile=Path(directory)/'Dockerfile'
        dockerfile.write_text(recipe(Path('deployment/containers/web/Dockerfile').read_text()))
        subprocess.run(['docker','build','--file',str(dockerfile),'--tag','projectpulse-web-ci:'+os.environ['GITHUB_SHA'],'.'],check=True)
