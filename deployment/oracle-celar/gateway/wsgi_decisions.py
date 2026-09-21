"""Add local decisions without replacing existing generation/OCR handlers."""
from wsgi import app
from laya_decisions import register

register(app)
