# ....... Work in progress .......


Summary
---

The project's goal is to perform an analysis of posts made in [TibiaBR forum] [1].
In order to accomplish this it was developed a robot that, through GET requests, extracts the page content using XPaths to read the tags of the HTML pages.


Features
---

+ Scalable process. Run as many instances as you like to speed up the process.
+ Available in the languages Brazilian Portuguese and English.
+ The solution is structured in smaller projects to allow easier maintenance.
+ Customizable configuration file.
+ Microsoft Message Queuing (MSMQ)
+ MongoDB (NoSQL Database)


Structure
---
 
### Bootstrapper

This is the first step that should be executed.
It is responsible for reading the configuration file and fetching the URLs for each requested section. When running this search, the process also captures information about the Section (Example: Number of Topics, Views).

### SectionsParser

This step processes the information captured above. Furthermore, it also captures more information about the Section and some initial details associated to the topics in this section.

### TopicsParser

This step captures all information related to the Topic class. It also captures some initial information about the comments.

### CommentsParser

Captures information about comments made on topics.

### Recorder

Last step of the project. This step saves the information you have acquired in a database or a private queue. It depends on the configuration set by the user.

### SharedLibrary

A class library to assist the classes used in all the steps above.


[1]: https://forums.tibiabr.com