#!/bin/bash
# Builds Inline Translator for macOS with the Xcode Command Line Tools (no Xcode project needed).
#
#   ./build.sh                             build the universal app and run the unit tests
#   ./build.sh --preview                   also render Settings, bubble and toast screenshots to build/preview
#   ./build.sh --package                   also create artifacts/inline-translator-mac.dmg and print its SHA-256
#   ./build.sh --create-signing-identity   create the local self-signed identity used to sign releases
#
# Releases are signed with a self-signed identity kept in its own keychain. Its fixed designated requirement
# keeps the Accessibility permission across updates; ad-hoc signatures change with every build and would
# make users grant the permission again after each update.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
repo="$(dirname "$root")"
build="$root/build"
app="$build/Inline Translator.app"
minimum="13.0"
identity="Inline Translator Signing"
keychain="$HOME/Library/Keychains/inline-translator-signing.keychain-db"
password_service="inline-translator-signing"

preview=false
package=false
for argument in "$@"; do
    case "$argument" in
        --preview) preview=true ;;
        --package) package=true ;;
        --create-signing-identity) create_identity=true ;;
        *) echo "Unknown option: $argument" >&2; exit 2 ;;
    esac
done

if [[ "${create_identity:-false}" == true ]]; then
    if [[ -f "$keychain" ]]; then echo "Signing keychain already exists: $keychain"; exit 0; fi
    secret="$(openssl rand -hex 24)"
    security add-generic-password -U -a "$USER" -s "$password_service" -w "$secret"
    security create-keychain -p "$secret" "$keychain"
    security set-keychain-settings "$keychain"
    work="$(mktemp -d)"
    trap 'rm -rf "$work"' EXIT
    printf '[req]\ndistinguished_name=dn\n[dn]\n[ext]\nbasicConstraints=critical,CA:false\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=critical,codeSigning\n' > "$work/openssl.cnf"
    openssl req -x509 -newkey rsa:2048 -nodes -days 7300 -keyout "$work/key.pem" -out "$work/cert.pem" \
        -subj "/CN=$identity" -extensions ext -config "$work/openssl.cnf" 2>/dev/null
    openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -out "$work/identity.p12" \
        -passout "pass:$secret" -name "$identity"
    security import "$work/identity.p12" -k "$keychain" -P "$secret" -T /usr/bin/codesign >/dev/null
    security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$secret" "$keychain" >/dev/null
    echo "Created \"$identity\" in $keychain"
    exit 0
fi

# Some Command Line Tools installs keep an outdated duplicate of the SwiftBridging module map, which breaks
# every Foundation import. Hide the stale copy from the compiler without touching the installation.
compiler_flags=()
stale_map="$(dirname "$(dirname "$(xcrun --find swiftc)")")/include/swift/module.modulemap"
if [[ -f "$stale_map" && -f "$(dirname "$stale_map")/bridging.modulemap" ]]; then
    mkdir -p "$build"
    : > "$build/empty.modulemap"
    printf '{"version":0,"case-sensitive":"false","roots":[{"type":"file","name":"%s","external-contents":"%s"}]}\n' \
        "$stale_map" "$build/empty.modulemap" > "$build/vfs-overlay.yaml"
    compiler_flags+=(-Xcc -ivfsoverlay -Xcc "$build/vfs-overlay.yaml")
fi

core=("$root"/Sources/Core/*.swift)
appsources=("$root"/Sources/App/*.swift)
compile() { # output arch sources...
    local output="$1" arch="$2"
    shift 2
    swiftc -O -swift-version 5 -target "$arch-apple-macos$minimum" ${compiler_flags[@]+"${compiler_flags[@]}"} -o "$output" "$@"
}

mkdir -p "$build"
echo "Building tests…"
compile "$build/tests" "$(uname -m)" "${core[@]}" "$root"/Tests/*.swift
"$build/tests" "$root/Sources"

echo "Building app…"
for arch in arm64 x86_64; do
    compile "$build/InlineTranslator-$arch" "$arch" "${core[@]}" "${appsources[@]}" "$root"/Sources/Main/*.swift &
done
wait
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
lipo -create -output "$app/Contents/MacOS/InlineTranslator" "$build/InlineTranslator-arm64" "$build/InlineTranslator-x86_64"
cp "$root/Info.plist" "$app/Contents/Info.plist"
printf 'APPL????' > "$app/Contents/PkgInfo"
cp "$repo/LICENSE" "$app/Contents/Resources/LICENSE"

iconset="$build/AppIcon.iconset"
rm -rf "$iconset"
mkdir -p "$iconset"
# macOS icons leave a margin around the artwork; the Windows artwork fills its square.
sips -Z 420 "$repo/src/InlineTranslator/Assets/app.png" --out "$build/icon-art.png" >/dev/null
sips --padToHeightWidth 512 512 "$build/icon-art.png" --out "$build/icon-512.png" >/dev/null
for size in 16 32 128 256 512; do
    sips -z $size $size "$build/icon-512.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    [[ $size -lt 512 ]] && sips -z $((size * 2)) $((size * 2)) "$build/icon-512.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

if [[ -f "$keychain" ]]; then
    secret="$(security find-generic-password -a "$USER" -s "$password_service" -w)"
    security unlock-keychain -p "$secret" "$keychain"
    # codesign finds identities only through the keychain search list, so add the signing keychain briefly.
    original_list=()
    while IFS= read -r line; do
        line="${line#"${line%%[![:space:]]*}"}"
        original_list+=("${line//\"/}")
    done < <(security list-keychains -d user)
    security list-keychains -d user -s "${original_list[@]}" "$keychain"
    status=0
    codesign --force --sign "$identity" --timestamp=none "$app" || status=$?
    security list-keychains -d user -s "${original_list[@]}"
    [[ $status -eq 0 ]] || exit $status
    echo "Signed with \"$identity\"."
else
    codesign --force --sign - "$app"
    echo "Signed ad hoc. Run ./build.sh --create-signing-identity once so Accessibility survives updates."
fi
codesign --verify --strict "$app"
echo "Built $app"

if [[ "$preview" == true ]]; then
    echo "Rendering previews…"
    compile "$build/preview" "$(uname -m)" "${core[@]}" "${appsources[@]}" "$root"/Preview/*.swift
    mkdir -p "$build/preview-images"
    "$build/preview" render "$build/preview-images"
fi

if [[ "$package" == true ]]; then
    mkdir -p "$repo/artifacts"
    stage="$build/dmg"
    rm -rf "$stage"
    mkdir -p "$stage"
    ditto "$app" "$stage/Inline Translator.app"
    ln -s /Applications "$stage/Applications"
    dmg="$repo/artifacts/inline-translator-mac.dmg"
    rm -f "$dmg"
    hdiutil create -quiet -volname "Inline Translator" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$dmg"
    echo "artifacts/inline-translator-mac.dmg SHA256 $(shasum -a 256 "$dmg" | cut -d' ' -f1)"
fi
