#!/bin/bash
# WeatherNative.xcframework(端末とシミュレーター)を作る。出力: build/WeatherNative.xcframework
set -eo pipefail
cd "$(dirname "$0")"
rm -rf build
xcodegen generate
for sdk in iphoneos iphonesimulator; do
  xcodebuild archive -project WeatherNative.xcodeproj -scheme WeatherNative -configuration Release \
    -sdk "$sdk" -archivePath "build/$sdk.xcarchive" SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES CODE_SIGNING_ALLOWED=NO
done
xcodebuild -create-xcframework \
  -framework build/iphoneos.xcarchive/Products/Library/Frameworks/WeatherNative.framework \
  -framework build/iphonesimulator.xcarchive/Products/Library/Frameworks/WeatherNative.framework \
  -output build/WeatherNative.xcframework
