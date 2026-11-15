#!/bin/sh
# Starts MasterSafe Authenticator inside the Flatpak. FLATPAK_ID tells the app that Flathub provides its updates.
exec /app/lib/mastersafe/MasterSafe.App "$@"
