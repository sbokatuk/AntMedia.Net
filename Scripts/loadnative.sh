#!/bin/sh

./Scripts/cleanup.sh native


#android
cd Native/src
git clone https://github.com/ant-media/WebRTC-Android-SDK
# curl -L https://github.com/ant-media/WebRTC-Android-SDK/archive/refs/heads/master.zip > Downloads/android.zip
mkdir WebRTC-Android-SDK/.idea
cp -R ../android/gradle.xml WebRTC-Android-SDK/.idea
cp -R ../android/local.properties WebRTC-Android-SDK
cp -R ../android/build.gradle WebRTC-Android-SDK/webrtc-android-framework

cd WebRTC-Android-SDK

rm -rf webrtc-android-framework/src/androidTest
rm -rf webrtc-android-framework/src/test

./gradlew webrtc-android-framework:build
./gradlew webrtc-android-framework:assemble

cd ../../..

mkdir -p Bindings/AntMedia.Net.Android/lib
cp -R Native/src/WebRTC-Android-SDK/webrtc-android-framework/build/outputs/aar/webrtc-android-framework-release.aar Bindings/AntMedia.Net.Android/lib/webrtc-android-framework.aar
AndroidVersion=$(cat Native/src/WebRTC-Android-SDK/webrtc-android-framework/build.gradle | grep "PUBLISH_VERSION = " | grep -Eo '([0-9]{1,}\.)+[0-9]{1,}');
sed -E -i "" "s/<ReleaseVersion>([0-9]{1,}\.)+[0-9]{1,}/<ReleaseVersion>${AndroidVersion}/" Bindings/AntMedia.Net.Android/AntMedia.Net.Android.csproj
echo "$AndroidVersion" > Native/AntMedia.Net.Android.version

cd Native/src/

#ios
git clone https://github.com/ant-media/WebRTC-iOS-SDK
# curl -L https://github.com/ant-media/WebRTC-iOS-SDK/archive/refs/heads/master.zip > Downloads/ios.zip

cd ../..

mkdir -p Bindings/AntMedia.Net.WebRTC.Dependency.iOS/lib
cp -R Native/src/WebRTC-iOS-SDK/WebRTC.xcframework Bindings/AntMedia.Net.WebRTC.Dependency.iOS/lib
mkdir -p Bindings/AntMedia.Net.iOS/lib
cp -R Native/src/WebRTC-iOS-SDK/WebRTCiOSSDK.xcframework Bindings/AntMedia.Net.iOS/lib

sed -E -i "" "s/<ReleaseVersion>([0-9]{1,}\.)+[0-9]{1,}/<ReleaseVersion>${AndroidVersion}/" Bindings/AntMedia.Net.iOS/AntMedia.Net.iOS.csproj
echo "$AndroidVersion" > Native/AntMedia.Net.iOS.version

echo "loading native libs done, Android: $AndroidVersion, iOS: iOSVersion"
