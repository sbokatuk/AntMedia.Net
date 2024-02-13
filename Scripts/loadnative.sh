#!/bin/sh

./Scripts/cleanup.sh native


#ios
cd Native/src
# git clone https://github.com/ant-media/WebRTC-Android-SDK
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
# git clone https://github.com/ant-media/WebRTC-iOS-SDK
# curl -L https://github.com/ant-media/WebRTC-iOS-SDK/archive/refs/heads/master.zip > Downloads/ios.zip


# cd opentok-ios-sdk-samples/Basic-Video-Chat-XCFramework
# 
# pod install
# 
# pod update



# cd ../../..
# 
# mv Downloads/opentok-ios-sdk-samples/Basic-Video-Chat-XCFramework/Pods/OTXCFramework/OpenTok.xcframework Bindings/OpenTok.Net.iOS/lib
# 
# iOSVersion=$(cat Downloads/opentok-ios-sdk-samples/Basic-Video-Chat-XCFramework/Podfile.lock | grep "OTXCFramework (=" | grep -Eo '([0-9]{1,}\.)+[0-9]{1,}');
# 
# sed -E -i "" "s/<ReleaseVersion>([0-9]{1,}\.)+[0-9]{1,}/<ReleaseVersion>${iOSVersion}.7/" Bindings/OpenTok.Net.iOS/OpenTok.Net.iOS.csproj
# 
# 
# echo "iOS lib downloaded!"
# #android
# 
# curl -L https://repo1.maven.org/maven2/com/opentok/android/opentok-android-sdk/maven-metadata.xml > Downloads/android.xml
# AndroidVersion=$(cat Downloads/android.xml | grep "<latest>" | grep -Eo '([0-9]{1,}\.)+[0-9]{1,}');
# # curl -L https://repo1.maven.org/maven2/com/opentok/android/opentok-android-sdk/$AndroidVersion/opentok-android-sdk-$AndroidVersion.aar > Downloads/opentok-android-sdk.aar
# # ignore previous row - download from parsed pom file
# curl -L https://repo1.maven.org/maven2/com/opentok/android/opentok-android-sdk/$AndroidVersion/opentok-android-sdk-$AndroidVersion.pom > Downloads/opentok-android-sdk.pom
# 
# sed -E -i "" "s/<ReleaseVersion>([0-9]{1,}\.)+[0-9]{1,}/<ReleaseVersion>${AndroidVersion}.1/" Bindings/OpenTok.Net.Android/OpenTok.Net.Android.csproj
# 
# groups=$(grep groupId Downloads/opentok-android-sdk.pom | sed -E 's/<.{0,1}groupId>//g' | awk '{print $1}');
# artifacts=$(grep artifactId Downloads/opentok-android-sdk.pom | sed -E 's/<.{0,1}artifactId>//g' | awk '{print $1}');
# versions=$(grep version Downloads/opentok-android-sdk.pom | sed -E 's/<.{0,1}version>//g' | tail -n +2 | awk '{print $1}');
# 
# count=$(grep groupId Downloads/opentok-android-sdk.pom | sed -E 's/<.{0,1}groupId>//g' | awk '{print $1}' | wc -l | awk '{print $1}')
# 
# for i in $(seq "$count")
# do
# currentGroup=$(echo "$groups" | sed -n ''$i' p');
# groupLink=${currentGroup//./\/}
# currentArtifact=$(echo "$artifacts" | sed -n ''$i' p');
# currentVersion=$(echo "$versions" | sed -n ''$i' p');
# 
# if [[ "$currentVersion" != "<?xml" ]]
# then
# if [[ $currentGroup =~ ^"com.google" ]] || [[ $currentGroup =~ ^"androidx" ]]
# then
# echo "Skip download $i of $count android lib, looks like default (google or android): $groupLink/$currentArtifact/$currentVersion"
# else
# echo "Downloading $i of $count android libs: https://repo1.maven.org/maven2/$groupLink/$currentArtifact/$currentVersion/$currentArtifact-$currentVersion.aar"
# curl -L https://repo1.maven.org/maven2/$groupLink/$currentArtifact/$currentVersion/$currentArtifact-$currentVersion.aar > Downloads/$currentArtifact.aar
# fi
# fi
# done
# 
# mv Downloads/*.aar Bindings/OpenTok.Net.Android/lib
# 
# rm -rf Downloads
# echo "Downloaded Opentok framework with version for ios $iOSVersion and android $AndroidVersion"
# echo "Verify that your android binding project referenced all dependency libs, they already downloaded into same folder Android/lib"
# 

#sharpie bind -scope OpenTok -output=CS -framework ./OpenTok.framework 


echo "loading native libs done, Android: $AndroidVersion, iOS: iOSVersion"
