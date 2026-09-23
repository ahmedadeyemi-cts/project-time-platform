const STAGES = Object.freeze({
  not_requested: 'Not yet queued', queued: 'Queued', scanning: 'Scanning',
  extracting: 'Extracting text', awaiting_ocr: 'Waiting for private OCR',
  embedding: 'Preparing search vectors', indexing: 'Indexing', retry_wait: 'Retry pending',
  ready: 'Processed; source verification required', needs_attention: 'Needs attention',
  failed: 'Processing failed', quarantined: 'Quarantined', cancelled: 'Cancelled',
  cancel_requested: 'Cancellation pending', unknown: 'Processing state unavailable'
});
const ERRORS = Object.freeze({
  document_processing_evidence_incomplete: 'The current version is missing a matching successful scan or extraction receipt. Its processing job must be repaired before classification.',
  document_extracted_text_missing: 'The processed version has no extracted text. Check the document processing job.',
  document_extracted_text_integrity_failed: 'The stored text does not match its extraction checksum. Reprocess the current document; do not bypass the integrity check.',
  document_source_integrity_failed: 'The stored file is unavailable or no longer matches the processed version. Reconcile the current document and process it again.',
  document_source_changed: 'The document changed during verification. Refresh its current processing state.',
  malware_detected: 'The scanner quarantined this document. It cannot be classified or used for AI.',
  private_ocr_not_configured: 'The document needs the approved private OCR service before classification.',
  legacy_word_extractor_unavailable: 'The deployed document worker is missing its approved legacy Word text extractor.',
  malware_scan_failed: 'The scanner did not verify this file as clean. Classification remains blocked.'
});
export function processingStageLabel(stage) { return STAGES[stage] || STAGES.unknown; }
export function processingMessage(value) {
  if (!value) return 'Select a document to verify its processing receipts.';
  if (value.readyForClassification === true) return 'Current file, scan receipt and extracted text verified. Ready for Laya classification.';
  return ERRORS[value.diagnosticCode] || `${processingStageLabel(value.stage)}. Classification waits for verified processing evidence.`;
}
export function shouldPollProcessing(value) {
  return !value?.readyForClassification && ['not_requested','queued','scanning','extracting','embedding','indexing','retry_wait','awaiting_ocr','cancel_requested'].includes(value?.stage);
}
