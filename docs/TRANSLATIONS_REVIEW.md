# Spanish translation review (M8)

Neutral Latin-American Spanish, informal *tú*. Source: `src/BTCPayServer.Plugins.MaxiAffiliates/Resources/Translations/es.json` (same keys as `en.json`, enforced by a test).

How to review: edit the Spanish column here or directly in `es.json` and I'll apply it. `{0}`, `{1}` … are placeholders filled at runtime (keep them). Terms chosen:

- payout → *pago*; commission → *comisión*; referral link → *enlace de referido*; hold period → *periodo de retención*
- clawback (deduction after a refund of an already paid commission) → *descuento por reembolso*
- carry over → *pasa al siguiente ciclo*; banned → *bloqueado*; invoice → *factura*
- Lightning, On-chain, LNURL, sats kept as is (standard in the Bitcoin community)

Admins can also tweak any string later in BTCPay: Server Settings → Translations → Spanish (edits there are kept).

242 strings (4 internal CSS class keys omitted).

| # | English | Spanish |
|---|---|---|
| 1 | Affiliates | Afiliados |
| 2 | Become an affiliate of {0} | Hazte afiliado de {0} |
| 3 | Share your referral link and earn a commission in bitcoin on every sale you refer. | Comparte tu enlace de referido y gana una comisión en bitcoin por cada venta que recomiendes. |
| 4 | Email | Correo electrónico |
| 5 | Name | Nombre |
| 6 | Name (optional) | Nombre (opcional) |
| 7 | Payout method | Método de pago |
| 8 | Lightning (Lightning address or LNURL) | Lightning (dirección Lightning o LNURL) |
| 9 | On-chain (Bitcoin address) | On-chain (dirección de Bitcoin) |
| 10 | Payout address | Dirección de pago |
| 11 | Sign up | Registrarme |
| 12 | Thank you for signing up | Gracias por registrarte |
| 13 | Your affiliate account is active. | Tu cuenta de afiliado está activa. |
| 14 | Your application was received and is waiting for approval by the store. | Recibimos tu solicitud; está pendiente de aprobación por parte de la tienda. |
| 15 | Your referral code: {0} | Tu código de referido: {0} |
| 16 | Public signup page: | Página pública de registro: |
| 17 | All statuses | Todos los estados |
| 18 | Search email, name or code | Buscar por correo, nombre o código |
| 19 | Filter | Filtrar |
| 20 | No affiliates yet. | Todavía no hay afiliados. |
| 21 | Code | Código |
| 22 | Payout | Pago |
| 23 | Status | Estado |
| 24 | Created | Creado |
| 25 | Actions | Acciones |
| 26 | Lightning | Lightning |
| 27 | On-chain | On-chain |
| 28 | Pending | Pendiente |
| 29 | Active | Activo |
| 30 | Suspended | Suspendido |
| 31 | Banned | Bloqueado |
| 32 | Approve | Aprobar |
| 33 | Reactivate | Reactivar |
| 34 | Suspend | Suspender |
| 35 | Ban | Bloquear |
| 36 | Ban this affiliate? This cannot be undone. | ¿Bloquear a este afiliado? Esta acción no se puede deshacer. |
| 37 | Affiliate {0} is now {1}. | El afiliado {0} ahora está en estado: {1}. |
| 38 | This action is not allowed for an affiliate in status {0}. | Esta acción no está permitida para un afiliado en estado {0}. |
| 39 | Enter a valid email address. | Ingresa un correo electrónico válido. |
| 40 | The affiliate program is not open for signups. | El programa de afiliados no está abierto a nuevos registros. |
| 41 | This payout method is not available. | Este método de pago no está disponible. |
| 42 | Enter a valid Lightning address (name@domain) or LNURL. | Ingresa una dirección Lightning válida (nombre@dominio) o un LNURL. |
| 43 | Enter a valid Bitcoin address for this network. | Ingresa una dirección de Bitcoin válida para esta red. |
| 44 | An affiliate with this email already exists. | Ya existe un afiliado con este correo electrónico. |
| 45 | View your affiliates | Ver tus afiliados |
| 46 | Allows viewing affiliates on all your stores. | Permite ver los afiliados de todas tus tiendas. |
| 47 | Allows viewing affiliates on the selected stores. | Permite ver los afiliados de las tiendas seleccionadas. |
| 48 | Manage your affiliates | Administrar tus afiliados |
| 49 | Allows approving, suspending and banning affiliates on all your stores. | Permite aprobar, suspender y bloquear afiliados en todas tus tiendas. |
| 50 | Allows approving, suspending and banning affiliates on the selected stores. | Permite aprobar, suspender y bloquear afiliados en las tiendas seleccionadas. |
| 51 | Referral links | Enlaces de referido |
| 52 | Landing page URL | URL de la página de destino |
| 53 | Save | Guardar |
| 54 | Referral link format: | Formato del enlace de referido: |
| 55 | Website helper script: | Script de ayuda para el sitio web: |
| 56 | Enter a valid http(s) URL for the landing page. | Ingresa una URL http(s) válida para la página de destino. |
| 57 | Referral settings saved. | Configuración de referidos guardada. |
| 58 | Commissions | Comisiones |
| 59 | Settings | Configuración |
| 60 | Affiliate program enabled | Programa de afiliados activado |
| 61 | Default commission (%) | Comisión predeterminada (%) |
| 62 | Pay commission on | Pagar comisión sobre |
| 63 | N (for First N payments) | N (para «Primeros N pagos») |
| 64 | Hold period (days) | Periodo de retención (días) |
| 65 | Commissions become payable after this period, which leaves time for refunds. | Las comisiones se vuelven pagables después de este periodo, lo que deja margen para reembolsos. |
| 66 | Commission base currency | Moneda base de la comisión |
| 67 | Affiliates and payouts | Afiliados y pagos |
| 68 | Approve new affiliates automatically | Aprobar nuevos afiliados automáticamente |
| 69 | Lightning payouts | Pagos por Lightning |
| 70 | On-chain payouts | Pagos on-chain |
| 71 | Approve payouts automatically | Aprobar pagos automáticamente |
| 72 | Minimum payout (sats) | Pago mínimo (sats) |
| 73 | Smaller balances carry over to the next cycle. | Los saldos menores pasan al siguiente ciclo. |
| 74 | Payout cycle | Ciclo de pagos |
| 75 | Payout day | Día de pago |
| 76 | Weekly: 0 = Sunday … 6. Monthly: 1–28. | Semanal: 0 = domingo … 6. Mensual: 1–28. |
| 77 | Commission rules per plan | Reglas de comisión por plan |
| 78 | A plan rule overrides the store default rule, which overrides the default commission above. | Una regla de plan tiene prioridad sobre la regla predeterminada de la tienda, y esta sobre la comisión predeterminada de arriba. |
| 79 | No rules yet. The default commission applies to every plan. | Todavía no hay reglas. La comisión predeterminada se aplica a todos los planes. |
| 80 | Plan | Plan |
| 81 | Commission | Comisión |
| 82 | Delete | Eliminar |
| 83 | All plans (store default rule) | Todos los planes (regla predeterminada de la tienda) |
| 84 | Type | Tipo |
| 85 | Value | Valor |
| 86 | Enabled | Activado |
| 87 | Add / update | Agregar / actualizar |
| 88 | Affiliate program settings | Configuración del programa de afiliados |
| 89 | Leave blank to use the store default currency ({0}). Currently used: {1}. | Déjalo en blanco para usar la moneda predeterminada de la tienda ({0}). Moneda actual: {1}. |
| 90 | Disabled | Desactivado |
| 91 | No commissions yet. | Todavía no hay comisiones. |
| 92 | Date | Fecha |
| 93 | Affiliate | Afiliado |
| 94 | Customer | Cliente |
| 95 | Invoice | Factura |
| 96 | Sale | Venta |
| 97 | Payable from | Pagable desde |
| 98 | body too large | cuerpo de la solicitud demasiado grande |
| 99 | invalid JSON | JSON no válido |
| 100 | offeringId and planId are required | offeringId y planId son obligatorios |
| 101 | plan not found | plan no encontrado |
| 102 | Affiliate program settings saved. | Configuración del programa de afiliados guardada. |
| 103 | Commission rule saved. | Regla de comisión guardada. |
| 104 | Commission rule deleted. | Regla de comisión eliminada. |
| 105 | The commission percentage must be between 0 and 100. | El porcentaje de comisión debe estar entre 0 y 100. |
| 106 | Enter how many payments earn a commission (N ≥ 1). | Indica cuántos pagos generan comisión (N ≥ 1). |
| 107 | The hold period must be between 0 and 365 days. | El periodo de retención debe estar entre 0 y 365 días. |
| 108 | The minimum payout cannot be negative. | El pago mínimo no puede ser negativo. |
| 109 | For weekly payouts choose a weekday. | Para pagos semanales elige un día de la semana. |
| 110 | For monthly payouts the day must be between 1 and 28. | Para pagos mensuales el día debe estar entre 1 y 28. |
| 111 | Enable at least one payout method. | Activa al menos un método de pago. |
| 112 | Enter a currency code such as USD, MXN or BTC, or leave it blank. | Ingresa un código de moneda como USD, MXN o BTC, o déjalo en blanco. |
| 113 | The commission value cannot be negative. | El valor de la comisión no puede ser negativo. |
| 114 | FirstPayment | Primer pago |
| 115 | Lifetime | De por vida |
| 116 | FirstN | Primeros N |
| 117 | Percent | Porcentaje |
| 118 | Flat | Monto fijo |
| 119 | Payable | Pagable |
| 120 | Paid | Pagado |
| 121 | Reversed | Revertido |
| 122 | Weekly | Semanal |
| 123 | Monthly | Mensual |
| 124 | Process holds and retries now | Procesar retenciones y reintentos ahora |
| 125 | Balances | Saldos |
| 126 | No open balances. | No hay saldos abiertos. |
| 127 | On hold | Retenido |
| 128 | Clawbacks | Descuentos por reembolso |
| 129 | Net | Neto |
| 130 | Next release | Próxima liberación |
| 131 | Payments waiting for an exchange rate | Pagos en espera de un tipo de cambio |
| 132 | Ledger | Registro |
| 133 | Reverse | Revertir |
| 134 | Minimum payout: {0} sats. Smaller or negative balances carry over to the next cycle. | Pago mínimo: {0} sats. Los saldos menores o negativos pasan al siguiente ciclo. |
| 135 | {0} attempts, next {1} | {0} intentos, próximo {1} |
| 136 | gave up, use Process now to retry | se abandonó; usa «Procesar ahora» para reintentar |
| 137 | Retried {0} waiting payments ({1} credited); {2} commissions became payable. | Se reintentaron {0} pagos en espera ({1} acreditados); {2} comisiones pasaron a pagables. |
| 138 | Commission reversed. | Comisión revertida. |
| 139 | Commission was already paid: {0} sats will be deducted from the next payout. | La comisión ya se había pagado: se descontarán {0} sats del próximo pago. |
| 140 | Nothing to reverse. | No hay nada que revertir. |
| 141 | First {0} payments | Primeros {0} pagos |
| 142 | First payment only | Solo el primer pago |
| 143 | First N payments | Primeros N pagos |
| 144 | Every payment (lifetime) | Todos los pagos (de por vida) |
| 145 | Due for payout | Listo para pagar |
| 146 | Below minimum, carries over | Bajo el mínimo, pasa al siguiente ciclo |
| 147 | Negative (clawback), carries over | Negativo (descuento), pasa al siguiente ciclo |
| 148 | Held: affiliate not active | Retenido: afiliado no activo |
| 149 | Nothing payable | Nada por pagar |
| 150 | Flat amount | Monto fijo |
| 151 | Percentage | Porcentaje |
| 152 | Invoice marked invalid | Factura marcada como inválida |
| 153 | Invoice refunded | Factura reembolsada |
| 154 | Subscription credit refunded | Crédito de suscripción reembolsado |
| 155 | Reversed manually | Revertida manualmente |
| 156 | Payouts | Pagos |
| 157 | Run payout cycle now | Ejecutar ciclo de pagos ahora |
| 158 | Payout cycles | Ciclos de pago |
| 159 | No payout cycles yet. | Todavía no hay ciclos de pago. |
| 160 | Cycle | Ciclo |
| 161 | Run at | Ejecutado el |
| 162 | Carried over | Pasa al siguiente ciclo |
| 163 | Approval | Aprobación |
| 164 | Commissions due report | Informe de comisiones por pagar |
| 165 | Cycle: {0}. Next scheduled run: {1} (UTC). Minimum payout: {2} sats. Approval: {3}. | Ciclo: {0}. Próxima ejecución programada: {1} (UTC). Pago mínimo: {2} sats. Aprobación: {3}. |
| 166 | automatic | automática |
| 167 | manual | manual |
| 168 | Due right now: {0} affiliates, {1} sats. | Por pagar ahora: {0} afiliados, {1} sats. |
| 169 | Manual run | Ejecución manual |
| 170 | Download CSV | Descargar CSV |
| 171 | Approve all and create payouts | Aprobar todo y crear los pagos |
| 172 | No affiliate reached the minimum payout in this cycle. | Ningún afiliado alcanzó el pago mínimo en este ciclo. |
| 173 | Destination | Destino |
| 174 | Gross | Bruto |
| 175 | Cancel | Cancelar |
| 176 | Commissions due | Comisiones por pagar |
| 177 | {0} payouts, {1} sats. {2} affiliates below the minimum of {3} sats carry over ({4} sats). | {0} pagos, {1} sats. {2} afiliados bajo el mínimo de {3} sats pasan al siguiente ciclo ({4} sats). |
| 178 | Awaiting approval | Pendiente de aprobación |
| 179 | Payout created | Pago creado |
| 180 | Sending | Enviando |
| 181 | Failed | Fallido |
| 182 | Cancelled | Cancelado |
| 183 | The affiliate program is disabled. | El programa de afiliados está desactivado. |
| 184 | Payout cycle created: {0} payouts due, {1} sats. | Ciclo de pagos creado: {0} pagos por realizar, {1} sats. |
| 185 | {0} payouts created, {1} failed. | {0} pagos creados, {1} fallidos. |
| 186 | Payout created in BTCPay. | Pago creado en BTCPay. |
| 187 | Payout failed: {0} | El pago falló: {0} |
| 188 | This payout was already processed. | Este pago ya fue procesado. |
| 189 | Payout cancelled; the amount returns to the affiliate's balance. | Pago cancelado; el monto vuelve al saldo del afiliado. |
| 190 | A panel link cannot be issued for this affiliate. | No se puede generar un enlace al panel para este afiliado. |
| 191 | Your {0} affiliate panel sign-in link | Tu enlace para entrar al panel de afiliados de {0} |
| 192 | Hello, | Hola: |
| 193 | Use this link to sign in to your affiliate panel for {0}. It works once and expires in {1} minutes. | Usa este enlace para entrar a tu panel de afiliados de {0}. Funciona una sola vez y vence en {1} minutos. |
| 194 | Sign in to the affiliate panel | Entrar al panel de afiliados |
| 195 | If you did not ask for this, you can ignore this email. | Si no lo solicitaste, puedes ignorar este correo. |
| 196 | This sign-in link is invalid or has expired. Request a new one. | Este enlace de acceso no es válido o ya venció. Solicita uno nuevo. |
| 197 | Your account is suspended; the payout address cannot be changed. | Tu cuenta está suspendida; no se puede cambiar la dirección de pago. |
| 198 | Your {0} affiliate payout address was changed | Se cambió tu dirección de pago de afiliado en {0} |
| 199 | Your payout address was changed to: {0} | Tu dirección de pago se cambió a: {0} |
| 200 | If you did not make this change, contact the store right away. | Si no hiciste este cambio, comunícate con la tienda de inmediato. |
| 201 | Payout address saved. Future payouts go to the new address. | Dirección de pago guardada. Los próximos pagos se enviarán a la nueva dirección. |
| 202 | Personal panel link (shown once, valid 30 days). Send it to the affiliate privately; issuing a new one revokes this one. | Enlace personal al panel (se muestra una sola vez y es válido por 30 días). Envíalo al afiliado de forma privada; si generas uno nuevo, este deja de funcionar. |
| 203 | Sign-in by email | Acceso por correo |
| 204 | Email not set up: issue panel links | Correo no configurado: genera enlaces al panel |
| 205 | Panel link | Enlace al panel |
| 206 | Affiliate panel: | Panel de afiliados: |
| 207 | Enter the email you signed up with. We will send you a one-time sign-in link. | Ingresa el correo con el que te registraste. Te enviaremos un enlace de acceso de un solo uso. |
| 208 | Email me a sign-in link | Enviarme un enlace de acceso |
| 209 | This store cannot send email yet. Ask the store for your personal affiliate panel link. | Esta tienda aún no puede enviar correos. Pídele a la tienda tu enlace personal al panel de afiliados. |
| 210 | {0} affiliate panel | Panel de afiliados de {0} |
| 211 | If this email belongs to an affiliate of this store, a sign-in link is on its way. The link works once and expires in 15 minutes. | Si este correo pertenece a un afiliado de esta tienda, te enviamos un enlace de acceso. El enlace funciona una sola vez y vence en 15 minutos. |
| 212 | Back | Volver |
| 213 | Check your email | Revisa tu correo |
| 214 | Continue to my affiliate panel | Continuar a mi panel de afiliados |
| 215 | Sign out | Cerrar sesión |
| 216 | Your application is waiting for approval. Your referral link starts working once the store approves it. | Tu solicitud está pendiente de aprobación. Tu enlace de referido empezará a funcionar cuando la tienda la apruebe. |
| 217 | Your account is suspended. New referrals are not credited and payouts are on hold. | Tu cuenta está suspendida. Los nuevos referidos no se acreditan y los pagos están en pausa. |
| 218 | Your referral link | Tu enlace de referido |
| 219 | Copy | Copiar |
| 220 | Balance | Saldo |
| 221 | Pending (on hold) | Pendiente (en retención) |
| 222 | Refund clawbacks | Descuentos por reembolso |
| 223 | Next payout | Próximo pago |
| 224 | No commissions yet. Share your link to start earning. | Todavía no tienes comisiones. Comparte tu enlace para empezar a ganar. |
| 225 | Sats | Sats |
| 226 | Payout history | Historial de pagos |
| 227 | No payouts yet. | Todavía no hay pagos. |
| 228 | Method | Método |
| 229 | Amount (sats) | Monto (sats) |
| 230 | State | Estado |
| 231 | Changes apply to future payouts. Payouts already approved keep their address. Saving signs you out on other devices. | Los cambios aplican a los próximos pagos. Los pagos ya aprobados conservan su dirección. Al guardar se cerrará tu sesión en otros dispositivos. |
| 232 | Referral code: {0} | Código de referido: {0} |
| 233 | Next release: {0} | Próxima liberación: {0} |
| 234 | Payouts are sent {0}. Minimum payout: {1} sats; smaller balances carry over. | Los pagos se envían {0}. Pago mínimo: {1} sats; los saldos menores pasan al siguiente ciclo. |
| 235 | Link clicks | Clics en el enlace |
| 236 | Referred customers | Clientes referidos |
| 237 | Sales | Ventas |
| 238 | Earned (sats) | Ganado (sats) |
| 239 | Paid out (sats) | Pagado (sats) |
| 240 | weekly | cada semana |
| 241 | monthly | cada mes |
| 242 | Processing | En proceso |

## New in v1: order confirmation callback (NEW, needs review)

Terms: callback kept as *callback* (common in Latin-American dev Spanish; alternative *notificación*), signing secret → *secreto de firma*, redeliver → *reenviar*.

| # | English | Español | |
|---|---|---|---|
| 243 | Attempts | Intentos | **NEW** |
| 244 | Callback URL | URL de callback | **NEW** |
| 245 | Copy your signing secret now. It will not be shown again. | Copia tu secreto de firma ahora. No se volverá a mostrar. | **NEW** |
| 246 | Delivery log | Registro de entregas | **NEW** |
| 247 | Event | Evento | **NEW** |
| 248 | Event redelivered. See the delivery log below. | Evento reenviado. Consulta el registro de entregas abajo. | **NEW** |
| 249 | Last response | Última respuesta | **NEW** |
| 250 | Must be https and a public host. Redirects are not followed. | Debe usar https y un host público. No se siguen redirecciones. | **NEW** |
| 251 | Next attempt: {0} | Próximo intento: {0} | **NEW** |
| 252 | No deliveries yet. | Aún no hay entregas. | **NEW** |
| 253 | No secret yet. Generate one before enabling the callback. | Aún no hay secreto. Genera uno antes de activar el callback. | **NEW** |
| 254 | Order callback | Callback de pedidos | **NEW** |
| 255 | Order confirmation callback | Callback de confirmación de pedidos | **NEW** |
| 256 | Redeliver | Reenviar | **NEW** |
| 257 | redelivery | reenvío | **NEW** |
| 258 | Send order confirmation callbacks | Enviar callbacks de confirmación de pedidos | **NEW** |
| 259 | Send test event | Enviar evento de prueba | **NEW** |
| 260 | Set on {0}. Regenerating it invalidates the old one immediately. | Creado el {0}. Al regenerarlo, el anterior deja de funcionar de inmediato. | **NEW** |
| 261 | Signing secret | Secreto de firma | **NEW** |
| 262 | test | prueba | **NEW** |
| 263 | When an invoice or subscription payment settles, the plugin sends a signed POST to your site with the order, plan, subscriber, amount and the affiliate (if any). Use it to confirm orders on your site. Every delivery is logged below. | Cuando se liquida una factura o un pago de suscripción, el plugin envía un POST firmado a tu sitio con el pedido, el plan, el suscriptor, el monto y el afiliado (si lo hay). Úsalo para confirmar pedidos en tu sitio. Cada entrega queda registrada abajo. | **NEW** |
| 264 | Regenerate secret | Regenerar secreto | **NEW** |
| 265 | Generate secret | Generar secreto | **NEW** |
| 266 | Callback settings saved. | Configuración del callback guardada. | **NEW** |
| 267 | Test event sent. See the delivery log below. | Evento de prueba enviado. Consulta el registro de entregas abajo. | **NEW** |
| 268 | Save a callback URL first. | Primero guarda una URL de callback. | **NEW** |
| 269 | Generate a signing secret before enabling the callback. | Genera un secreto de firma antes de activar el callback. | **NEW** |
| 270 | Enter a valid callback URL. | Ingresa una URL de callback válida. | **NEW** |
| 271 | The callback URL must use https. | La URL de callback debe usar https. | **NEW** |
| 272 | The callback URL must not contain credentials or a fragment. | La URL de callback no debe contener credenciales ni un fragmento. | **NEW** |
| 273 | The callback URL must point to a public host. | La URL de callback debe apuntar a un host público. | **NEW** |
| 274 | Delivered | Entregado | **NEW** |
| 275 | Retrying | Reintentando | **NEW** |
