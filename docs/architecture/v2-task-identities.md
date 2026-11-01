# Nodalis 2.0 — identités persistantes des tâches Markdown

**Statut :** incrément `develop` prêt pour qualification Windows. Le
raccordement aux mesures des risques #199 reste une phase distincte, à
réaliser uniquement avec des références non ambiguës.

## 1. Pourquoi une nouvelle identité ?

En v1.9, le `TaskItem.Id` d'une checkbox était recalculé à partir du
GUID du document, du numéro de ligne et du contenu de la tâche. Il
changeait après réordonnancement, déplacement, modification du texte
ou des métadonnées. Cet identifiant d'occurrence était pratique pour
la navigation mais **impropre comme clé étrangère** dans un risque,
une réserve de Gate ou un document de suivi.

Nodalis 2.0 introduit un **GUID portable propre à chaque checkbox**,
stocké au bout de la ligne source comme commentaire HTML, par exemple :

```markdown
- [ ] Vérifier les contrôles | Responsable: Alice <!-- nodalis:task:04f73451-24d8-4521-925e-efb977775167 -->
```

Le commentaire est invisible dans le rendu Markdown et n'apparaît ni
dans le nom de la tâche, ni parmi ses métadonnées. L'éditeur de
métadonnées le conserve, la modification du statut de checkbox le
conserve, et il **reste identique lorsque la ligne est déplacée**
avec son commentaire dans le même fichier ou un autre fichier du
workspace. Le `TaskItem.PersistentId` expose exclusivement ce GUID
**validé** ; `TaskItem.Id` reprend le GUID lorsqu'il existe, sinon
continue d'utiliser l'identifiant d'occurrence hérité. Ne jamais
enregistrer un `TaskItem.Id` de tâche `PersistentId == null`
dans une relation stable.

Le marqueur de tâche diffère de l'identité de bloc titre déjà
existante `<!-- nodalis:block:GUID -->`. Les deux syntaxes sont
complémentaires et ne doivent pas être confondues.

## 2. Comportement utilisateur

- **Nouvelle tâche** depuis Nodalis : création explicite d'une
  checkbox avec un GUID stable neuf dès l'enregistrement.
- **Actions de réunion promues vers les tâches du projet** :
  la nouvelle checkbox reçoit son propre GUID. Si elle existait
  déjà, la mise à jour conserve son GUID.
- **Ancienne tâche** : elle reste entièrement lisible avec son
  ancienne identité d'occurrence ; **aucune lecture, ouverture,
  rafraîchissement ou indexation ne réécrit les documents**.
- Dans la vue **Tâches**, une ancienne checkbox possède le bouton
  **Attribuer un ID** ; après confirmation, Nodalis insère
  *uniquement* son commentaire de GUID. Une checkbox déjà identifiée
  propose à la place **Copier l'ID**.
- Une attribution exige un projet actif, une source de projet
  indexée avec son GUID de document, une référence réellement
  contenue dans le workspace et une checkbox source non ambiguë.
  Les fichiers situés sous jonction ou lien symbolique sont refusés.

## 3. Parsing, collisions, compatibilité

`TaskPersistentIdentityCodec` inspecte les checkboxes hors
blocs de code clôturés par ``` ou ~~~, respecte les titres et le
texte Markdown d'origine, et signale les marqueurs malformés.
Les identifiants répétés dans le **même document** ou dans des
**documents distincts** sont signalés dans
`TaskCollection.IdentityDiagnostics`. Les occurrences concernées
restent consultables, mais n'exposent **pas de `PersistentId`
utilisable pour les liens** : elles repassent provisoirement à
l'identité dérivée. Nodalis ne les corrige jamais sans consentement
humain.

L'attribution à une ancienne tâche vérifie le document sur disque,
sa localisation exacte et la présence d'une seule occurrence de la
ligne attendue, puis sauvegarde avec `TextDocumentSession`.
Une source modifiée, un doublon ou un projet archivé produit une
erreur explicite au lieu d'une réattribution ou fusion silencieuse.

Les autres usages de l'éditeur, les tâches globales/applicatives,
les métadonnées `Responsable/Échéance/Priorité/Statut/Tags`, les
promotions de réunions, les vues Kanban et l'affichage des tâches
doivent continuer à fonctionner avec les tâches sans marqueur.

## 4. Cas de qualification Windows

1. Créer une tâche depuis la vue Tâches ; vérifier le GUID caché dans
   le Markdown et l'ID affiché dans l'interface.
2. Ajouter manuellement une ancienne checkbox dans un fichier
   du projet, ouvrir le registre et vérifier qu'**aucune écriture**
   n'a lieu. Cliquer **Attribuer un ID** puis accepter.
3. Réordonner les lignes, ajouter des lignes au-dessus, modifier les
   métadonnées, puis cocher/décocher. L'ID doit rester exactement
   le même ; aucune autre tâche ne doit être modifiée.
4. Copier volontairement le même GUID dans deux documents. Le
   diagnostic de collision doit apparaître dans le catalogue ;
   les deux occurrences doivent être refusées comme cibles
   stables, sans suppression du texte source.
5. Tester des GUID malformés, des checkbox en blocs de code, des
   sources modifiées entre la lecture et la validation, et des
   projets archivés.
6. Vérifier le clavier, le DPI, les fichiers Markdown déjà ouverts
   dans l'éditeur, le Worktree et l'absence de freeze.

**Relations et navigation :** le contrat
`(risque GUID, version de plan GUID, mesure, tâche PersistentId)`
est maintenant opérationnel avec accord humain et journal d'événements
réversibles dans la fiche RIS. Les tâches ne sont **jamais**
cochées par cette relation et l'état des mesures n'est jamais
déduit de la case Markdown. Les backlinks
**« Tâches → Risques liés… »** et le parcours
**« Risques → Tâches liées… → Ouvrir la tâche »** sont désormais
disponibles et n'écrivent aucun fichier lors de la navigation.
Voir [relations et navigation RIS/tâches](v2-risk-task-links.md).

**Mise en production :** interdite avant la qualification visuelle et
l'accord utilisateur ; aucun merge `main`, tag ou release 2.0.
