import test from 'node:test';
import assert from 'node:assert/strict';
import { moneyCents, manualCharge } from '../src/manual-invoice-model.mjs';
test('partial then full deduct local and external invoices', () => {
  assert.equal(manualCharge('10000','3000','1000',0,'partial'),2000);
  assert.equal(manualCharge('10000','6000','1000',2000,'partial'),3000);
  assert.equal(manualCharge('10000','','1000',5000,'final'),4000);
});
test('already invoiced charges, overbilling and fractional cents are rejected', () => {
  assert.equal(manualCharge('10000','5000','1000',4000,'partial'),null);
  assert.equal(manualCharge('10000','11000','0',0,'partial'),null);
  for(const value of ['', '1.001', '-1', '1e3', 'Infinity']) assert.equal(moneyCents(value),null);
});
test('amount entry uses integer cents', () => {
  assert.equal(manualCharge('0.30','','0.10','0.10','final'),0.10);
  assert.equal(moneyCents('0.01'),1);
});
