import hmac
from flask import Flask,jsonify,request
from runtime import read_secret
from laya_protocol import register,exchange,normalized,DecisionError
app=Flask(__name__)
app.config.update(MAX_CONTENT_LENGTH=16384,PROPAGATE_EXCEPTIONS=False)
token=read_secret()
@app.before_request
def _authenticate():
    if request.path=='/health/live' and request.method=='GET':return jsonify(status='live')
    if request.path=='/health/ready' and request.method=='GET':
        try:normalized(exchange({'op':'health'},seconds=3),health=True);return jsonify(status='ready')
        except Exception:return jsonify(status='not_ready'),503
    supplied=request.headers.get('Authorization','')
    if len(supplied)>4200 or not hmac.compare_digest(supplied.encode(),('Bearer '+token).encode()):return jsonify(error={'code':'unauthorized'}),401
    if request.headers.get('X-Pulse-AI-Privacy-Boundary')!='private_pulse_runtime_only':return jsonify(error={'code':'privacy_boundary_required'}),403
@app.after_request
def headers(response):
    response.headers['Cache-Control']='no-store';response.headers['X-Content-Type-Options']='nosniff';return response
@app.errorhandler(Exception)
def failure(error):return jsonify(ok=False,error={'code':'decision_request_failed'}),500
register(app)
