# ....... Work in progress .......


Summary
---

The project's goal is to perform an analysis of posts made in [TibiaBR forum][1].
In order to accomplish that it was developed a robot that, through GET requests, extracts the page content using XPaths to read the tags of the HTML pages.


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
 
### SectionsParser

This is the first step that should be executed.
It is responsible for reading the configuration file and fetching the URLs for each requested section. When running this search, the process also captures all information about the Section.

__Input__: Configuration File

__Output__: Records saved in the "Sections" Collection  and in the "forumtibiabr_sections" Queue

### TopicsParser

This step captures all information related to the Topic class. 

__Input__: "forumtibiabr_sections" Queue

__Output__: Records saved in the "Topics" Collection  and in the "forumtibiabr_topics" Queue


### CommentsParser

Captures information about comments made on topics.

__Input__: "forumtibiabr_topics" Queue

__Output__: Records saved in the "Comments" Collection


### SharedLibrary

A class library to assist the classes used in all the steps above.


[1]: https://forums.tibiabr.com