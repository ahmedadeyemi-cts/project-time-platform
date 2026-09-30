"""Content-free liveness or ClamAV loaded-signature readiness."""
import sys
from urllib.request import urlopen
if len(sys.argv) == 2 and sys.argv[1] == "clamav":
    from clamd_client import ClamdClient
    ClamdClient().ready()
else:
    with urlopen("http://127.0.0.1:8082/health/live", timeout=3) as response:
        if response.status != 200:
            sys.exit(1)
