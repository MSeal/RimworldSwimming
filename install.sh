#!/bin/sh

TARGET_DIR="/mnt/c/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods/SwimmingKit"
rm -rf "${TARGET_DIR}"
mkdir "${TARGET_DIR}"
cp -r About "${TARGET_DIR}"
cp -r 1.0 "${TARGET_DIR}"
cp -r 1.1 "${TARGET_DIR}"
cp -r 1.2 "${TARGET_DIR}"
cp -r 1.3 "${TARGET_DIR}"
cp -r Defs "${TARGET_DIR}"
cp -r Patches "${TARGET_DIR}"
cp -r Languages "${TARGET_DIR}"

