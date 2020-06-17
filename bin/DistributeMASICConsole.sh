#!/usr/bin/env bash

mkdir -p ./Dist/MASICConsole

cp Console/Release/MASIC_Console.exe ./Dist/MASICConsole
cp Console/Release/*.dll             ./Dist/MASICConsole
cp ../Readme.md                      ./Dist/MASICConsole
cp ../RevisionHistory.txt            ./Dist/MASICConsole

rm -f MASIC_Console_Program.zip && zip -r MASIC_Console_Program.zip Dist/MASICConsole/* 
