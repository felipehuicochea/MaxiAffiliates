/*! MaxiAffiliates website helper (MIT). Captures ?ref=CODE (first click, 30 days) and starts a BTCPay plan checkout carrying it.
 * <script src="https://YOUR-BTCPAY/plugins/maxiaffiliates/js/maxiaff.js" data-store="STORE_ID"></script>
 * <button data-maxiaff-offering="OFFERING_ID" data-maxiaff-plan="PLAN_ID">Subscribe</button>
 * JS: MaxiAffiliates.getRef(); MaxiAffiliates.checkout({offeringId, planId, email, successRedirectUrl});
 */
(function (w, d) {
  'use strict';
  var KEY = 'maxiaff_ref', DAYS = 30, MS = DAYS * 864e5;
  var me = d.currentScript;
  var server = me ? new URL(me.src).origin : '';
  var storeId = me ? me.getAttribute('data-store') : null;
  var CODE_RE = /^[A-Za-z0-9_-]{1,32}$/;

  function read() {
    try {
      var v = JSON.parse(w.localStorage.getItem(KEY) || 'null');
      if (v && CODE_RE.test(v.code) && Date.now() - v.ts <= MS) return v;
    } catch (e) { }
    var m = d.cookie.match(/(?:^|; )maxiaff_ref=([^;]+)/);
    if (m) { var p = decodeURIComponent(m[1]).split('.'); if (CODE_RE.test(p[0]) && Date.now() - (+p[1]) <= MS) return { code: p[0], ts: +p[1] }; }
    return null;
  }
  function write(code) {
    var v = { code: code.toUpperCase(), ts: Date.now() };
    try { w.localStorage.setItem(KEY, JSON.stringify(v)); } catch (e) { }
    d.cookie = 'maxiaff_ref=' + encodeURIComponent(v.code + '.' + v.ts) + '; Max-Age=' + (DAYS * 86400) + '; Path=/; SameSite=Lax';
    return v;
  }
  // First click wins: only store the incoming ref if there is no valid earlier one.
  function capture() {
    var r = new URLSearchParams(w.location.search).get('ref');
    var cur = read();
    if (!cur && r && CODE_RE.test(r)) cur = write(r);
    return cur;
  }
  var state = capture();

  function checkout(opts) {
    opts = opts || {};
    var sid = opts.storeId || storeId;
    var body = { offeringId: opts.offeringId, planId: opts.planId, ref: state ? state.code : null, email: opts.email || null, successRedirectUrl: opts.successRedirectUrl || null };
    return fetch((opts.server || server) + '/plugins/maxiaffiliates/' + encodeURIComponent(sid) + '/checkout', {
      // BTCPay's global CORS policy is AllowAnyOrigin (no credentials), so the ref travels in the body.
      // text/plain keeps it a CORS "simple request" (no preflight); the server parses JSON from any content type.
      method: 'POST', credentials: 'omit', headers: { 'Content-Type': 'text/plain' }, body: JSON.stringify(body)
    }).then(function (r) { if (!r.ok) throw new Error('checkout failed: ' + r.status); return r.json(); })
      .then(function (j) { if (opts.redirect !== false) w.location.href = j.url; return j; });
  }

  d.addEventListener('click', function (e) {
    var el = e.target.closest && e.target.closest('[data-maxiaff-plan]');
    if (!el) return;
    e.preventDefault();
    checkout({ offeringId: el.getAttribute('data-maxiaff-offering'), planId: el.getAttribute('data-maxiaff-plan'),
               email: el.getAttribute('data-maxiaff-email'), successRedirectUrl: el.getAttribute('data-maxiaff-success') });
  });

  w.MaxiAffiliates = { getRef: function () { return state ? state.code : null; }, checkout: checkout, version: '1' };
})(window, document);
