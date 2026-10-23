#!/usr/bin/env python3
"""Run the real RabbitMQ redelivery test against the reserved local E2E broker."""
import json
import os
import subprocess
from pathlib import Path
from urllib.parse import quote


ROOT = Path(__file__).resolve().parents[1]
CONTAINER = "evangelionerpv2_e2e_rabbitmq"
NETWORK = "evangelionerpv2_e2e_net"


def main():
    info = json.loads(subprocess.check_output(["docker", "inspect", CONTAINER], text=True))[0]
    network = json.loads(subprocess.check_output(["docker", "network", "inspect", NETWORK], text=True))[0]
    ports = info["NetworkSettings"].get("Ports", {})
    if (
        not info["Config"]["Image"].startswith("rabbitmq:")
        or NETWORK not in info["NetworkSettings"]["Networks"]
        or not network.get("Internal")
        or any(bindings for bindings in ports.values() if bindings)
    ):
        raise SystemExit("Refusing a RabbitMQ container outside the dedicated E2E network.")

    settings = dict(value.split("=", 1) for value in info["Config"]["Env"] if "=" in value)
    username = settings.get("RABBITMQ_DEFAULT_USER")
    password = settings.get("RABBITMQ_DEFAULT_PASS")
    host = info["NetworkSettings"]["Networks"][NETWORK]["IPAddress"]
    if not username or not password or not host:
        raise SystemExit("The isolated E2E broker is missing its address or credentials.")

    environment = os.environ.copy()
    environment["ERP_E2E_RABBITMQ_URI"] = (
        f"amqp://{quote(username, safe='')}:{quote(password, safe='')}@{host}:5672/%2F"  # NOSONAR: runner requires a dedicated Docker internal network with no published ports.
    )
    return subprocess.run(
        [
            "dotnet",
            "test",
            "EvangelionERPV2.Test/EvangelionERPV2.Test.csproj",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~RabbitMQRetryRedeliveryTests",
        ],
        cwd=ROOT,
        env=environment,
        check=False,
    ).returncode


if __name__ == "__main__":
    raise SystemExit(main())
