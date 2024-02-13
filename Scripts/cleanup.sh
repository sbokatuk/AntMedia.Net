#!/bin/sh


if [ -z "$1" ]
then
echo "for cleaning add parameter"
else

if [[ "$1" == "all" ]]
then
rm -rf Bindings
mkdir Bindings
rm -rf Native
mkdir Native
rm -rf Downloads
mkdir Downloads
rm -rf NugetPackages
mkdir NugetPackages
fi
if [[ "$1" == "nuget" ]]
then
rm -rf NugetPackages/*.nupkg
fi
if [[ "$1" == "version" ]]
then
sed -E -i "" "s=1-beta1=$BUILD=" NugetPackages/$2.nuspec
sed -E -i "" "s=1-beta1=$BUILD=" NugetPackages/$2.Mac.nuspec
sed -E -i "" "s=1-beta1=$BUILD=" NugetPackages/$2.Android.nuspec
sed -E -i "" "s=1-beta1=$BUILD=" NugetPackages/$2.iOS.nuspec
fi

if [[ "$1" == "native" ]]
then
rm -rf Native/src
mkdir -p Native/src
rm -rf Bindings/AntMedia.Net.Android/lib
mkdir -p Bindings/AntMedia.Net.Android/lib
rm -rf Bindings/AntMedia.Net.iOS/lib
mkdir -p Bindings/AntMedia.Net.iOS/lib
rm -rf Bindings/AntMedia.Net.Mac/lib
mkdir -p Bindings/AntMedia.Net.Mac/lib
rm -rf Downloads
mkdir Downloads
fi

fi



