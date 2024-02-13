#!/bin/sh

# ./Scripts/cleanup.sh native


#android
cd Native/src

if [ -d WebRTC-Android-SDK ]
then
cd WebRTC-Android-SDK
git clean -fd
git pull
cd ..
else
git clone https://github.com/ant-media/WebRTC-Android-SDK
fi
cd WebRTC-Android-SDK
AndroidCommitOld=$(cat ../../AntMedia.Net.Android.commit)
AndroidCommit=$(git rev-parse --verify HEAD)
echo "$AndroidCommit"
echo "$AndroidCommitOld"
if [[ "$AndroidCommit" == "$AndroidCommitOld" ]]
then
echo "Android commit does not changed. skip building"
cd ..
else
cd ..
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
cd Native/src/WebRTC-Android-SDK
git rev-parse --verify HEAD > ../../AntMedia.Net.Android.commit
cd ..
fi


#ios
if [ -d WebRTC-iOS-SDK ]
then
cd WebRTC-iOS-SDK
git clean -fd
git pull
cd ..
else
git clone https://github.com/ant-media/WebRTC-iOS-SDK
fi
cd WebRTC-iOS-SDK
iOSCommitOld=$(cat ../../AntMedia.Net.iOS.commit)
iOSCommit=$(git rev-parse --verify HEAD)
echo "$iOSCommit"
echo "$iOSCommitOld"
if [[ "$iOSCommit" == "$iOSCommitOld" ]]
then
echo "iOS commit does not changed. skip building"
else
# curl -L https://github.com/ant-media/WebRTC-iOS-SDK/archive/refs/heads/master.zip > Downloads/ios.zip

cd ../../..

mkdir -p Bindings/AntMedia.Net.WebRTC.Dependency.iOS/lib
cp -R Native/src/WebRTC-iOS-SDK/WebRTC.xcframework Bindings/AntMedia.Net.WebRTC.Dependency.iOS/lib
mkdir -p Bindings/AntMedia.Net.iOS/lib
cp -R Native/src/WebRTC-iOS-SDK/WebRTCiOSSDK.xcframework Bindings/AntMedia.Net.iOS/lib

sed -E -i "" "s/<ReleaseVersion>([0-9]{1,}\.)+[0-9]{1,}/<ReleaseVersion>${AndroidVersion}/" Bindings/AntMedia.Net.iOS/AntMedia.Net.iOS.csproj
echo "$AndroidVersion" > Native/AntMedia.Net.iOS.version
cd Native/src/WebRTC-iOS-SDK
git rev-parse --verify HEAD > ../../AntMedia.Net.iOS.commit
fi
echo "loading native libs done, Android: $AndroidVersion, iOS: iOSVersion"
