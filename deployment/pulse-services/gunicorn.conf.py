bind='0.0.0.0:8082'
workers=1
threads=2
timeout=210
keepalive=2
limit_request_line=2048
limit_request_fields=32
limit_request_field_size=8190
accesslog=None
errorlog='-'
worker_tmp_dir='/tmp'
def post_fork(server,worker):
    from runtime import harden
    harden('gateway')
