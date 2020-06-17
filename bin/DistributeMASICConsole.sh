#!/usr/bin/env bash

mkdir -p ./Dist/MASICConsole

cp Console/Release/* ./Dist/MASICConsole
cp ../Readme.md                      ./Dist/MASICConsole
cp ../RevisionHistory.txt            ./Dist/MASICConsole

cp ../Docs/*.txt ./Dist/MASICConsole
cp ../Lib/RawFileReaderLicense.doc ./Dist/MASICConsole
cp ../Docs/MASICParameters.xml ./Dist/MASICConsole

rm -f Dist/MASIC_Console_Program.zip && pushd Dist/MASICConsole && zip -r ../MASIC_Console_Program.zip * && popd
