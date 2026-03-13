#!/usr/bin/env bash

DEFAULT_ADDRESS="192.168.1.128"
localAddress="192.168.1.127"

declare -a servers=("$DEFAULT_ADDRESS" "$localAddress")

function pingAddress() {
    local address="$1"
    local output
    output=$(ping -c 1 "$address" 2>&1)
    exitCode=$?

    echo "Exit code for ping at address $address: $exitCode"

    if [[ $exitCode -ne 0 ]]; then
        echo "$output"
    fi
}

for address in "${servers[@]}"; do
    pingAddress "$address"
done
