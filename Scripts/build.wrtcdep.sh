#!/bin/sh

# ./Scripts/build.wrtcdep.sh BUILD="1-beta1" VERSION="6.0.0" IOSVERSION="6.0.0" ANDROIDVERSION="6.0.0" MACVERSION="6.0.0"
for ARGUMENT in "$@"
do
   KEY=$(echo $ARGUMENT | cut -f1 -d=)

   KEY_LENGTH=${#KEY}
   VALUE="${ARGUMENT:$KEY_LENGTH+1}"

   export "$KEY"="$VALUE"
done

if [ -z "$VERSION" ]
then
echo "No target Argument for nuget version"
else
echo "$IOSVERSION" > Bindings/AntMedia.Net.WebRTC.Dependency.iOS/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$IOSVERSION.7/" Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-ios/<TargetFramework>net7.0-ios/" Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$IOSVERSION.8/" Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-ios/<TargetFramework>net8.0-ios/" Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.iOS/AntMedia.Net.WebRTC.Dependency.iOS.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf wrtcdepios
unzip -n -q AntMedia.Net.WebRTC.Dependency.iOS.$IOSVERSION.7.nupkg -d wrtcdepios
unzip -n -q AntMedia.Net.WebRTC.Dependency.iOS.$IOSVERSION.8.nupkg -d wrtcdepios
rm AntMedia.Net.WebRTC.Dependency.iOS.$IOSVERSION.7.nupkg
rm AntMedia.Net.WebRTC.Dependency.iOS.$IOSVERSION.8.nupkg
cd ..
echo "ios part nugets generated"
echo "$MACVERSION" > Bindings/AntMedia.Net.WebRTC.Dependency.Mac/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$MACVERSION.7/" Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-maccatalyst/<TargetFramework>net7.0-maccatalyst/" Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$MACVERSION.8/" Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-maccatalyst/<TargetFramework>net8.0-maccatalyst/" Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.Mac/AntMedia.Net.WebRTC.Dependency.Mac.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf wrtcdepmac
unzip -n -q AntMedia.Net.WebRTC.Dependency.Mac.$MACVERSION.7.nupkg -d wrtcdepmac
unzip -n -q AntMedia.Net.WebRTC.Dependency.Mac.$MACVERSION.8.nupkg -d wrtcdepmac
rm AntMedia.Net.WebRTC.Dependency.Mac.$MACVERSION.7.nupkg
rm AntMedia.Net.WebRTC.Dependency.Mac.$MACVERSION.8.nupkg
cd ..
echo "mac part nugets generated"
echo "$ANDROIDVERSION" > Bindings/AntMedia.Net.WebRTC.Dependency.Android/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$ANDROIDVERSION.7/" Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-android/<TargetFramework>net7.0-android/" Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$ANDROIDVERSION.8/" Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-android/<TargetFramework>net8.0-android/" Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj
dotnet pack Bindings/AntMedia.Net.WebRTC.Dependency.Android/AntMedia.Net.WebRTC.Dependency.Android.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf wrtcdepandroid
unzip -n -q AntMedia.Net.WebRTC.Dependency.Android.$ANDROIDVERSION.7.nupkg -d wrtcdepandroid
unzip -n -q AntMedia.Net.WebRTC.Dependency.Android.$ANDROIDVERSION.8.nupkg -d wrtcdepandroid
rm AntMedia.Net.WebRTC.Dependency.Android.$ANDROIDVERSION.7.nupkg
rm AntMedia.Net.WebRTC.Dependency.Android.$ANDROIDVERSION.8.nupkg
cd ..
echo "android part nugets generated"
cd NugetPackages

cp -R wrtcdepandroid/Readme.md wrtcdepandroid/Readme.txt
cp -R wrtcdepmac/Readme.md wrtcdepmac/Readme.txt
cp -R wrtcdepios/Readme.md wrtcdepios/Readme.txt
cp -R wrtcdep/Readme.md wrtcdep/Readme.txt

# mkdir Voice/native
# mkdir Voice/native/lib
# mkdir Voice/native/lib/ios
# cp -R webrtc/lib/net8.0-android34.0/webrtc.aar webrtc/native/lib/android
# 
# rm webrtc/lib/net8.0-android34.0/webrtc.aar
# rm webrtc/lib/net7.0-android33.0/webrtc.aar 


sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$ANDROIDVERSION.$BUILD/" AntMedia.Net.WebRTC.Dependency.Android.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$IOSVERSION.$BUILD/" AntMedia.Net.WebRTC.Dependency.iOS.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$MACVERSION.$BUILD/" AntMedia.Net.WebRTC.Dependency.Mac.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$VERSION.$BUILD/" AntMedia.Net.WebRTC.Dependency.nuspec

nuget pack AntMedia.Net.WebRTC.Dependency.Android.nuspec
nuget pack AntMedia.Net.WebRTC.Dependency.iOS.nuspec
nuget pack AntMedia.Net.WebRTC.Dependency.Mac.nuspec
nuget pack AntMedia.Net.WebRTC.Dependency.nuspec

rm -rf wrtcdepandroid
rm -rf wrtcdepios
rm -rf wrtcdepmac

# if  [ -z "$3" ]
# then
# echo "package ready"
# unzip -n -q OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg -d webrtc
# else 
# dotnet nuget push OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg --api-key $3 --source https://api.nuget.org/v3/index.json --timeout 3000000
# # rm OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg
# fi
# cd ..
fi