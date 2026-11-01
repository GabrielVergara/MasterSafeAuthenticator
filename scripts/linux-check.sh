#!/bin/bash
# Runs the tests and the real app on Linux in a throwaway container, from the repository root:
#   docker run --rm -v "${PWD}:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/scripts/linux-check.sh
set -u
apt-get -qq update >/dev/null
apt-get -qq install -y xvfb libfontconfig1 libice6 libsm6 libx11-6 libxcursor1 libxext6 libxi6 libxrandr2 libxrender1 libgl1 procps >/dev/null 2>&1

mkdir /work
tar -C /src --exclude=bin --exclude=obj --exclude=artifacts --exclude=.vs -cf - . | tar -C /work -xf -
git config --global --add safe.directory /work
cd /work

echo "=== TESTS ==="
dotnet test -c Debug -v q --nologo 2>&1 | tail -8

echo "=== PUBLISH ==="
dotnet publish src/MasterSafe.App/MasterSafe.App.csproj -c Release -r linux-x64 --self-contained -o /tmp/app -v q --nologo 2>&1 | tail -3
ls /tmp/app | grep -i -E '^MasterSafe.App$'

echo "=== REAL APP AS AN ORDINARY USER ==="
useradd -m tester
chmod -R a+rX /tmp/app
su tester -c 'cd ~ && (ulimit -c unlimited; Xvfb :99 -screen 0 1280x800x24 >/dev/null 2>&1 & sleep 2; DISPLAY=:99 HOME=/home/tester /tmp/app/MasterSafe.App >/tmp/app.log 2>&1 & echo $! >/tmp/app.pid; sleep 12)'
pid=$(cat /tmp/app.pid)
if kill -0 "$pid" 2>/dev/null; then
    echo "running: yes"
else
    echo "running: NO"
    tail -20 /tmp/app.log
fi
echo "core limit: $(grep 'Max core file size' /proc/$pid/limits)"
echo "memory file owner (root means protected): $(stat -c %U /proc/$pid/mem)"
su tester -c "head -c 16 /proc/$pid/mem >/dev/null 2>/tmp/err; echo read exit \$?; cat /tmp/err"
su tester -c "ls -ld /home/tester/.local/share/MasterSafe /home/tester/.config/MasterSafe 2>/dev/null"
kill "$pid"
sleep 1
echo "after end: $(kill -0 "$pid" 2>/dev/null && echo still-running || echo ended)"
