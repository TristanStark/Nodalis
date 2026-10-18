# Documents composites — socle 1.6 (develop)

## Syntaxe

L'inclusion d'un document existant ou d'une section se fait sur une ligne Markdown entière :

~~~md
![[Document]]
![[Document#Tests]]
![[GUID-du-document#Tests]]
~~~

Le GUID est préférable pour les nouveaux liens. Les alias et noms qualifiés sont résolus uniquement s'ils identifient un seul document. Les liens ordinaires sans point d'exclamation, les images et les relations métier restent inchangés.

## Extraction et protection

Une section commence au titre demandé et inclut les sous-titres jusqu'au prochain titre de niveau identique ou supérieur. Les titres situés dans des blocs de code ne comptent pas. En cas de titre dupliqué, Nodalis signale la section ambiguë au lieu de choisir arbitrairement. L'aperçu virtuel ne réécrit jamais les documents sources.

Le moteur est borné à 8 niveaux, 256 fragments et 2 millions de caractères inclus. Les cycles, cibles ambiguës, traversées de répertoire, fichiers manquants et annulations sont traités sans exécution de contenu ni accès réseau. La provenance GUID est conservée et les arêtes d'inclusion restent distinctes des dépendances causales d'analyse d'impact.

Voir les issues [#135](https://github.com/TristanStark/Nodalis/issues/135), [#136](https://github.com/TristanStark/Nodalis/issues/136) et [#137](https://github.com/TristanStark/Nodalis/issues/137). L'intégration UI, les exports et les invalidations ciblées sont traités dans les étapes suivantes de l'EPIC #126.

## Aperçu et export

Depuis **Ctrl+P**, « Insérer un document ou une section » propose une cible canonique, puis sa section. L'éditeur garde uniquement le marqueur ; l'aperçu résout les fragments à la lecture, avec un lien de provenance « Ouvrir dans la source ».

« Exporter le document composite » propose quatre modes : **Markdown avec références**, **Markdown résolu**, **HTML résolu**, **DOCX résolu**. Les exports sont explicites, locaux et produisent un instantané figé ; ils ne modifient jamais la source. HTML est échappé contre les injections. Le DOCX est généré en OpenXML standard sans NuGet et prend en charge la structure de base (paragraphes, titres, tableaux simples). Les styles riches, images attachées et rendus Mermaid doivent être vérifiés/améliorés avant la qualification finale #141.
