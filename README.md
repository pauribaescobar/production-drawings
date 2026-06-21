# production-drawings

Herramienta para generar PDFs de producción a partir de:

* un PDF de pedido
* un ZIP con los planos DFT asociados

El objetivo es rellenar automáticamente los planos del taller con la información del pedido y dejar un PDF listo para imprimir.

## Flujo general

1. Subir el PDF del pedido.
2. Subir el ZIP de planos.
3. Procesar el contenido.
4. Generar un PDF final por piezas únicas.
5. Descargar e imprimir.

## Reglas confirmadas

### Archivos relevantes

* Solo los archivos `.dft` se procesan.
* Los `.stp`, `.step` y equivalentes se ignoran por completo.
* El ZIP puede contener archivos extra sin problema, siempre que estén todos los DFT necesarios.

### Identidad de pieza

La identidad de una pieza es:

* referencia
* dimensiones
* material
* tratamiento

Si alguno de esos valores cambia, se genera una salida distinta.

### Normalización

Toda la comparación textual se normaliza a minúsculas.

### Relación referencia-planos

El nombre del DFT corresponde a la referencia de la pieza.

Ejemplo:

* `4004B` -> `4004B.dft`

### Cantidades

Si dos líneas del pedido tienen la misma identidad, no se suman sus cantidades en un único valor.

Se conserva la estructura original:

* `2 + 2`

en lugar de:

* `4`

### Información obligatoria en el plano

Cada plano generado debe mostrar siempre:

* cantidad
* material
* fecha de entrega
* número de pedido

### Información adicional

Cuando exista, también debe mostrarse:

* tratamiento
* dimensiones

La regla de total de dimensiones queda apartada por ahora y no se aplicará en la primera versión.

### Material especial

Cuando el material sea `Inox 316L`, debe ir subrayado en amarillo fosforito.

### Número de pedido

El número de pedido se extrae del PDF, en la zona superior derecha donde aparece `Order NO:`. Ese valor es común a todas las piezas del pedido.

### Fecha de entrega

La fecha de entrega se extrae de la línea correspondiente a cada pieza.

### Faltantes

Si falta un DFT necesario en el ZIP, el sistema debe avisar al usuario para que pueda pedir ese plano al proveedor.

## Punto abierto

La parte todavía no cerrada es cómo procesar los DFT:

* qué formato exacto usan
* si se pueden leer programáticamente
* si se pueden renderizar directamente
* si se pueden exportar automáticamente
* si hace falta automatización de Solid Edge

La hipótesis preferida es investigar primero la opción A, pero esto sigue siendo una decisión técnica pendiente.

## Fuera de alcance por ahora

* Regla de total de dimensiones
* Criterios de aceptación medibles
* Límites operativos
* Persistencia de archivos, logs o resultados intermedios

## Documentación relacionada

* [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md)
* [TECH_SPEC.md](TECH_SPEC.md)
* [BUSINESS_RULES.md](BUSINESS_RULES.md)
