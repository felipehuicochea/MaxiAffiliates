# MaxiAffiliates: integración con tu sitio web

Todos los ejemplos usan `https://BTCPAY` (tu BTCPay Server) y `STORE_ID`. English: [INTEGRATION.md](INTEGRATION.md).

## 1. Enlaces de referido
`https://BTCPAY/aff/STORE_ID/CODIGO`
- **Código activo:**
  - Guarda una cookie propia y HttpOnly `maxiaff_ref_STORE_ID` en el dominio de BTCPay, válida por 30 días; gana el primer clic.
  - Redirige a la **URL de la página de destino** de la tienda (en Tienda → Afiliados) con `?ref=CODIGO`.
  - Si el visitante ya tiene un clic anterior válido, se conserva y se transmite ese código anterior.
- **Código desconocido, pendiente, suspendido o bloqueado:** redirige a la página de destino sin `ref` y no guarda cookie.
- **Sin URL de destino configurada:** 404.
- También puedes compartir cualquier página de tu sitio como `https://tu.sitio/pagina?ref=CODIGO`. El script de ayuda lo detecta.

## 2. Script de ayuda para el sitio (no requiere backend)
```html
<script src="https://BTCPAY/plugins/maxiaffiliates/js/maxiaff.js" data-store="STORE_ID"></script>
<button data-maxiaff-offering="OFFERING_ID" data-maxiaff-plan="PLAN_ID">Suscribirme</button>
```
- **Captura del ref:** en cualquier página con `?ref=CODIGO`, el script guarda el código en `localStorage` y en una cookie `maxiaff_ref` de tu sitio, por 30 días. Nunca sobrescribe un código anterior (primer clic).
- **Botones de compra:** al hacer clic en un elemento con `data-maxiaff-plan` se crea un checkout de plan en BTCPay con el ref y se redirige al comprador. Atributos opcionales: `data-maxiaff-email`, `data-maxiaff-success`.
- **API de JS:**
  - `MaxiAffiliates.getRef()`
  - `MaxiAffiliates.checkout({offeringId, planId, email, successRedirectUrl, redirect})`, que devuelve una promesa de `{checkoutId, url, attributed}`.

## 3. Endpoint de checkout (script, enlaces simples o tu backend)
- **JSON:** `POST https://BTCPAY/plugins/maxiaffiliates/STORE_ID/checkout`, cuerpo `{"offeringId","planId","ref","email","successRedirectUrl"}`, con cualquier content type.
  - Devuelve `{checkoutId, url, attributed}`.
  - Un plan desconocido da 404; si faltan ids, 400.
- **Sin JS:** `GET https://BTCPAY/plugins/maxiaffiliates/STORE_ID/checkout?offeringId=…&planId=…&ref=…` responde con un 302 al checkout. Aquí también se usa la cookie de BTCPay.
- **Cómo se elige el código:** la cookie de BTCPay y el `ref` explícito se combinan con la regla de primer clic (30 días). El código se descarta si el afiliado no está Activo, o si el correo del comprador es el mismo que el del afiliado (autorreferido).
- **CORS:** BTCPay aplica su política global AllowAnyOrigin (sin credenciales), así que las llamadas entre sitios envían el ref en el cuerpo.

## 4. Contrato de metadatos (para backends que llaman a Greenfield directamente)
Si tu backend crea los checkouts por su cuenta (Greenfield `POST /api/v1/plan-checkout`, o `POST /api/v1/stores/{id}/invoices` para ventas únicas), incluye el código de referido:
```json
"invoiceMetadata":       { "maxiaffRef": "CODIGO" },
"newSubscriberMetadata": { "maxiaffRef": "CODIGO" }
```
(Para facturas simples usa `metadata.maxiaffRef`.) El endpoint del plugin también agrega `maxiaffAffiliateId`. La atribución ocurre cuando la factura se liquida, y en ese momento se vuelve a verificar el estado del afiliado y el autorreferido contra el correo del comprador.
