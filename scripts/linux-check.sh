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
su tester -c "ls -ld /home/tester/.local/share/MasterSafe" || echo "DATA FOLDER NOT WHERE IT SHOULD BE"

echo "=== ONE RUNNING COPY ==="
su tester -c 'cd /tmp && DISPLAY=:99 HOME=/home/tester timeout 20 /tmp/app/MasterSafe.App >/tmp/second.log 2>&1; echo "second start ended with exit code $? (0 is right, 124 means it did not end)"'
kill -0 "$pid" 2>/dev/null && echo "first copy: still running" || echo "first copy: GONE"
echo "copies now: $(pgrep -c -f /tmp/app/MasterSafe.App)"
ls -l /home/tester/.local/share/MasterSafe/instance.lock 2>/dev/null

kill "$pid"
sleep 1
echo "after end: $(kill -0 "$pid" 2>/dev/null && echo still-running || echo ended)"

echo "=== A NEW START AFTER THE FIRST ENDED ==="
su tester -c 'DISPLAY=:99 HOME=/home/tester /tmp/app/MasterSafe.App >/tmp/third.log 2>&1 & echo $! >/tmp/app3.pid; sleep 8'
pid3=$(cat /tmp/app3.pid)
kill -0 "$pid3" 2>/dev/null && echo "third start: running, the lock was released" || { echo "third start: NOT RUNNING"; tail -5 /tmp/third.log; }
kill "$pid3" 2>/dev/null
