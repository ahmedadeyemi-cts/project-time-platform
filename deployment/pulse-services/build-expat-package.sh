#!/bin/sh
# Build the actual fixed upstream library; never relabel an older binary.
set -eu
export LC_ALL=C
source_url='https://github.com/libexpat/libexpat/releases/download/R_2_8_5/expat-2.8.5.tar.xz'
source_hash='1e727b8933ec51a77a9a9d9afcf8e688bce45d907c13e36ab7393fe36e703182'
mkdir -p /tmp/expat-source /tmp/expat-package
curl --fail --show-error --location --proto '=https' --proto-redir '=https' "$source_url" -o /tmp/expat.tar.xz
printf '%s  %s\n' "$source_hash" /tmp/expat.tar.xz | sha256sum -c -
tar -xJf /tmp/expat.tar.xz --strip-components=1 -C /tmp/expat-source --no-same-owner
cd /tmp/expat-source
triplet="$(dpkg-architecture -qDEB_HOST_MULTIARCH)"
./configure --prefix=/usr --libdir="/usr/lib/$triplet" --disable-static --without-docbook --without-examples --without-xmlwf --with-tests
make -j2
make check
make DESTDIR=/tmp/expat-package install
# Ship the runtime library, its license and source provenance, not build tools.
rm -rf /tmp/expat-package/usr/include /tmp/expat-package/usr/lib/pkgconfig /tmp/expat-package/usr/lib/cmake
rm -rf "/tmp/expat-package/usr/lib/$triplet/pkgconfig" "/tmp/expat-package/usr/lib/$triplet/cmake"
find /tmp/expat-package -type f -name '*.la' -delete
mkdir -p /tmp/expat-package/DEBIAN /tmp/expat-package/usr/share/doc/libexpat1
cp COPYING /tmp/expat-package/usr/share/doc/libexpat1/copyright
cp /build/expat-source.json /tmp/expat-package/usr/share/doc/libexpat1/pulse-source.json
cat > /tmp/expat-package/DEBIAN/control <<EOF
Package: libexpat1
Source: expat
Version: 2.8.5-0pulse1
Architecture: $(dpkg --print-architecture)
Maintainer: Pulse Release Automation
Section: libs
Priority: optional
Multi-Arch: same
Depends: libc6 (>= 2.36)
Description: Fixed upstream Expat 2.8.5 for private Pulse document processing
 Built from the checksum-verified upstream release; upstream tests passed.
 Source provenance and license are retained under /usr/share/doc/libexpat1.
EOF
dpkg-deb --build --root-owner-group /tmp/expat-package /libexpat1-pulse.deb
printf '%s\n' 'PULSE_EXPAT_SOURCE_AND_TESTS=VERIFIED version=2.8.5'
