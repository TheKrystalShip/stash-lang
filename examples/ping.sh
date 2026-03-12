#!/usr/bin/env bash

DEFAULT_ADDRESS="192.168.1.128"
localAddress="192.168.1.127"

declare -a servers=("$DEFAULT_ADDRESS" "$localAddress")

function ping_address() {
    local address="$1"
    local output
    output=$(ping -c 1 "$address" 2>&1)
    exitCode=$?

    echo "Exit code for ping at address $address: $exitCode"

    if [[ $exitCode -ne 0 ]]; then
        echo "$output"
    fi
}

for server in "${servers[@]}"; do
    ping_address "$server"
done
