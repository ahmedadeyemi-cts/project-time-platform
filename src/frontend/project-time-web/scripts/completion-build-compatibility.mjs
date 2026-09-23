// Keep source contracts strict while preserving the original build error.
// No failed compilation is converted into a successful build by these hooks.
export function normalizeCompletionCommercialRegion(code, id) {
  const before = '<details className="m0423-commercial" aria-label="ConnectWise SELL commercial source">';
  const after = '<details className="m0423-commercial" aria-label="Commercial source">';
  const count = code.split(before).length - 1;
  if (count !== 1) {
    throw new Error(`[customer-source-authority] Expected exactly one Module 042 commercial disclosure in ${id}; found ${count}.`);
  }
  return code.replace(before, after);
}

export function createSourceTransactionPlugin({ prepare, restore, verify }) {
  let compilationFailed = false;
  return {
    name: 'celar-ai-production-source-transaction',
    apply: 'build',
    async buildStart() {
      compilationFailed = false;
      await prepare();
    },
    async buildEnd(error) {
      compilationFailed = Boolean(error);
      if (compilationFailed) await restore();
    },
    async closeBundle() {
      try {
        // Vite also closes the bundle after a failed transform. In that case,
        // report that original failure rather than replace it with missing output.
        if (!compilationFailed) await verify();
      } finally {
        await restore();
      }
    }
  };
}
